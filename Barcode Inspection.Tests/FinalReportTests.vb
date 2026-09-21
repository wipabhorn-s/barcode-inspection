Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports Npgsql

<TestClass>
Public Class FinalReportQueryTests

    <TestMethod>
    Public Sub PassQuery_JoinsCombinedCartons()
        Dim query = FinalReportRepository.BuildPassQuery(New FinalReportFilter())

        StringAssert.Contains(query.Sql, "LEFT JOIN barcode_inspection.record_combined_carton c ON c.barcode = p.barcode")
        StringAssert.Contains(query.Sql, "ORDER BY p.inspection_date ASC, p.id ASC")
        Assert.AreEqual(0, query.Parameters.Count)
    End Sub

    <TestMethod>
    Public Sub PassQuery_AddsEveryFilterAsParameter()
        Dim filter = New FinalReportFilter With {
            .ProductName = "P1", .WorkOrder = "WO1", .CartonNo = 7, .Barcode = "B1", .Hinge = "H1", .CombinedCartonNo = 3,
            .DateFrom = New Date(2026, 9, 30), .DateTo = New Date(2026, 9, 1)}

        Dim query = FinalReportRepository.BuildPassQuery(filter)

        CollectionAssert.AreEquivalent(
            {"ProductName", "WorkOrder", "CartonNo", "Barcode", "BarcodeHinge", "CombinedCartonNo", "DateFrom", "DateTo"},
            query.Parameters.Keys.ToArray())
        Assert.AreEqual(7, query.Parameters("CartonNo"))
        Assert.AreEqual(New Date(2026, 9, 1), query.Parameters("DateFrom"), "reversed dates are put in order")
        StringAssert.Contains(query.Sql, "AND c.combined_carton_no = @CombinedCartonNo")
    End Sub

    <TestMethod>
    Public Sub CombinedCartonOption_ShowsCartonAndWorkOrders()
        Assert.AreEqual("12 (WO1, WO2)", New CombinedCartonOption With {.CartonNo = 12, .WorkOrders = "WO1, WO2"}.ToString())
    End Sub

End Class

''' <summary>Final Data queries against a real PostgreSQL; see <see cref="DatabaseIntegrationTests"/>.</summary>
<TestClass>
<TestCategory("Integration")>
Public Class FinalReportIntegrationTests

    Private Const Product As String = "IT-FINAL"
    Private _db As DbConnectionFactory
    Private _repo As FinalReportRepository

    <TestInitialize>
    Public Sub SetUp()
        Dim connectionString = Environment.GetEnvironmentVariable("BARCODE_INSPECTION_TEST_DB")
        If String.IsNullOrWhiteSpace(connectionString) Then Assert.Inconclusive("BARCODE_INSPECTION_TEST_DB is not set.")

        _db = New DbConnectionFactory(connectionString)
        _repo = New FinalReportRepository(_db)
        Execute("INSERT INTO barcode_inspection.product_spec (product_name, barcode_spec, quantity) VALUES (@p, 'K7L@@@@', 50)")
    End Sub

    <TestCleanup>
    Public Sub CleanUp()
        If _db Is Nothing Then Return
        For Each table In {"record_combined_carton", "record_barcode_pass", "record_barcode_fail"}
            Execute($"DELETE FROM barcode_inspection.{table} WHERE product_name = @p")
        Next
        Execute("DELETE FROM barcode_inspection.pass_part_counter WHERE work_order LIKE 'IT-%'")
        Execute("DELETE FROM barcode_inspection.fail_part_counter WHERE work_order LIKE 'IT-%'")
        Execute("DELETE FROM barcode_inspection.product_spec WHERE product_name = @p")
    End Sub

    Private Sub Execute(sql As String)
        Using conn = _db.TryOpenConnection()
            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.Parameters.AddWithValue("@p", Product)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Private Async Function SaveAsync(barcode As String, workOrder As String, carton As Integer, isPass As Boolean) As Task
        Await New InspectionRepository(_db).SaveAsync(New InspectionData With {
            .ProductName = Product, .WorkOrder = workOrder, .Barcode = barcode, .CartonNo = carton,
            .LineNo = 1, .OperatorID = "OP", .IsPass = isPass})
    End Function

    Private Sub Combine(barcode As String, workOrder As String, cartonNo As Integer)
        Execute($"INSERT INTO barcode_inspection.record_combined_carton (combined_carton_no, product_name, work_order, barcode, operator_id)
                  VALUES ({cartonNo}, @p, '{workOrder}', '{barcode}', 'OP2')")
    End Sub

    <TestMethod>
    Public Async Function Search_FiltersAndShowsCombinedDetails() As Task
        Await SaveAsync("F0001", "IT-WO1", 1, True)
        Await SaveAsync("F0002", "IT-WO1", 2, True)
        Await SaveAsync("F0003", "IT-WO2", 1, True)
        Combine("F0001", "IT-WO1", 9)
        Combine("F0003", "IT-WO2", 9)

        Dim byCarton = Await _repo.SearchPassAsync(New FinalReportFilter With {.ProductName = Product, .WorkOrder = "IT-WO1", .CartonNo = 1})
        Assert.AreEqual(1, byCarton.Rows.Count)
        Assert.AreEqual(9, CInt(byCarton.Rows(0)("combined_carton_no")))
        Assert.AreEqual("OP2", byCarton.Rows(0)("combined_operator_id"))

        Dim byCombined = Await _repo.SearchPassAsync(New FinalReportFilter With {.ProductName = Product, .CombinedCartonNo = 9})
        Assert.AreEqual(2, byCombined.Rows.Count)

        Dim cartons = Await _repo.GetCombinedCartonsAsync(Product)
        Assert.AreEqual("9 (IT-WO1, IT-WO2)", cartons.Single().ToString())
        CollectionAssert.AreEquivalent({"IT-WO1", "IT-WO2"}, Await _repo.GetWorkOrdersAsync(Product))
    End Function

    <TestMethod>
    Public Async Function Failures_AreListedPerProduct() As Task
        Await SaveAsync("F0001", "IT-WO1", 1, False)
        Await SaveAsync("F0001", "IT-WO1", 1, False)

        Dim failures = Await _repo.GetFailuresAsync(Product)

        Assert.AreEqual(2, failures.Rows.Count)
        Await _repo.DeleteFailByIdsAsync({Convert.ToInt64(failures.Rows(0)("id"))})
        Assert.AreEqual(1, (Await _repo.GetFailuresAsync(Product)).Rows.Count)
    End Function

    <TestMethod>
    Public Async Function DeleteCombined_KeepsThePassRecord() As Task
        Await SaveAsync("F0001", "IT-WO1", 1, True)
        Combine("F0001", "IT-WO1", 5)

        Await _repo.DeleteCombinedAsync({"F0001"})

        Dim rows = Await _repo.SearchPassAsync(New FinalReportFilter With {.Barcode = "F0001"})
        Assert.AreEqual(1, rows.Rows.Count, "pass record is still there")
        Assert.IsTrue(IsDBNull(rows.Rows(0)("combined_carton_no")), "but no longer in a combined carton")

        Await _repo.DeletePassByIdsAsync({Convert.ToInt64(rows.Rows(0)("id"))})
        Assert.AreEqual(0, (Await _repo.SearchPassAsync(New FinalReportFilter With {.Barcode = "F0001"})).Rows.Count)
    End Function

End Class
