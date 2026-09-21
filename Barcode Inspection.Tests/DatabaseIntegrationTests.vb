Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports Npgsql

''' <summary>
''' Runs the repositories' real SQL against PostgreSQL. Skipped unless BARCODE_INSPECTION_TEST_DB points to a
''' throw-away database created with Database/create_schema.sql. Every test deletes the rows it created.
''' </summary>
<TestClass>
<TestCategory("Integration")>
Public Class DatabaseIntegrationTests

    Private Const Product As String = "IT-PRODUCT"
    Private Const WorkOrder As String = "IT-WO"

    Private _db As DbConnectionFactory

    <TestInitialize>
    Public Sub SetUp()
        Dim connectionString = Environment.GetEnvironmentVariable("BARCODE_INSPECTION_TEST_DB")
        If String.IsNullOrWhiteSpace(connectionString) Then Assert.Inconclusive("BARCODE_INSPECTION_TEST_DB is not set.")

        _db = New DbConnectionFactory(connectionString)
        Execute("INSERT INTO barcode_inspection.product_spec (product_name, barcode_spec, quantity, running_check) VALUES (@p, 'K7L@@@@', 50, TRUE)")
    End Sub

    <TestCleanup>
    Public Sub CleanUp()
        If _db Is Nothing Then Return
        For Each table In {"record_barcode_dimension", "record_barcode_appearance", "record_barcode_pass", "record_barcode_fail"}
            Execute($"DELETE FROM barcode_inspection.{table} WHERE product_name = @p")
        Next
        For Each counter In {"dimension_part_counter", "appearance_part_counter", "pass_part_counter", "fail_part_counter"}
            Execute($"DELETE FROM barcode_inspection.{counter} WHERE work_order LIKE 'IT-%'")
        Next
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

    Private Shared Function Part(barcode As String, isPass As Boolean, Optional remark As String = Nothing) As InspectionData
        Return New InspectionData With {
            .ProductName = Product, .WorkOrder = WorkOrder, .Barcode = barcode, .CartonNo = 1,
            .LineNo = 3, .OperatorID = "OP1", .IsPass = isPass, .Remark = remark}
    End Function

    <TestMethod>
    Public Async Function ProductSpec_IsReadWithFlags() As Task
        Dim spec = Await New ProductSpecRepository(_db).GetAsync(Product)

        Assert.AreEqual("K7L@@@@", spec.BarcodeSpec)
        Assert.AreEqual(50, spec.Quantity)
        Assert.IsTrue(spec.RunningCheck)
        Assert.IsFalse(spec.Modulus34)
        Assert.IsNull(Await New ProductSpecRepository(_db).GetAsync("no such product"))
    End Function

    <TestMethod>
    Public Async Function Station_InsertNumbersPartsAndReadsBack() As Task
        Dim repo As New StationRepository(_db, StationInfo.Dimension)

        Await repo.InsertAsync(Part("K7L0001", True))
        Await repo.InsertAsync(Part("K7L0002", False, "burr"))

        Assert.IsTrue(Await repo.ExistsAsync("K7L0001"))
        Assert.IsFalse(Await repo.ExistsAsync("K7L9999"))
        Assert.AreEqual(2, Await repo.CountAsync(Product, WorkOrder))
        CollectionAssert.AreEqual({WorkOrder}, Await repo.GetWorkOrdersAsync(Product))

        Dim recent = Await repo.GetRecentRecordsAsync(Product, WorkOrder)
        Assert.AreEqual(2, recent.Rows.Count)
        Assert.AreEqual(1, CInt(recent.Rows(0)("part_no")), "part_no comes from the trigger, oldest first")
        Assert.AreEqual("PASS", recent.Rows(0)("dimension_result"))
        Assert.AreEqual("FAIL", recent.Rows(1)("dimension_result"))
        Assert.AreEqual("burr", recent.Rows(1)("remark"))

        Dim latest = Await repo.GetLatestRecordAsync("K7L0002")
        Assert.AreEqual(2, CInt(latest.Rows(0)("part_no")))
    End Function

    <TestMethod>
    Public Async Function Station_SearchSplitsPassAndFail() As Task
        Dim repo As New StationRepository(_db, StationInfo.Appearance)
        Await repo.InsertAsync(Part("K7L0001", True))
        Await repo.InsertAsync(Part("K7L0002", False))
        Await repo.InsertAsync(Part("K7L0003", True))

        Dim filter = New StationSearchFilter With {.ProductName = Product, .WorkOrder = WorkOrder, .DateFrom = Date.Today, .DateTo = Date.Today}
        Dim passed = Await repo.SearchAsync(filter, passed:=True)
        Dim failed = Await repo.SearchAsync(filter, passed:=False)

        Assert.AreEqual(2, passed.Rows.Count)
        Assert.AreEqual(1, failed.Rows.Count)
        Assert.AreEqual(1, (Await repo.SearchAsync(New StationSearchFilter With {.Barcode = "K7L0003"}, True)).Rows.Count)
    End Function

    <TestMethod>
    Public Async Function Station_DeleteByIdAndByBarcode() As Task
        Dim repo As New StationRepository(_db, StationInfo.Dimension)
        Await repo.InsertAsync(Part("K7L0001", True))
        Await repo.InsertAsync(Part("K7L0002", True))
        Await repo.InsertAsync(Part("K7L0003", True))

        Dim rows = Await repo.SearchAsync(New StationSearchFilter With {.Barcode = "K7L0001"}, True)
        Await repo.DeleteByIdsAsync({Convert.ToInt64(rows.Rows(0)("id"))})
        Await repo.DeleteByBarcodesAsync({"K7L0002"})

        Assert.AreEqual(1, Await repo.CountAsync(Product, WorkOrder))
        Assert.IsTrue(Await repo.ExistsAsync("K7L0003"))
    End Function

    <TestMethod>
    Public Async Function FinalInspection_SavesAndRejectsSecondPass() As Task
        Dim repo As New InspectionRepository(_db)

        Await repo.SaveAsync(Part("K7L0001+A", True))
        Await repo.SaveAsync(Part("K7L0002", False, "Duplicate barcode"))

        Assert.IsTrue(Await repo.PassRecordExistsAsync("K7L0001+A"))
        Assert.IsTrue(Await repo.PassRecordWithPrefixExistsAsync("K7L0001"))
        Assert.IsFalse(Await repo.PassRecordWithPrefixExistsAsync("K7L000_"), "LIKE wildcards in a barcode must be literal")
        Assert.AreEqual(1, (Await repo.GetCartonRecordsAsync(Product, WorkOrder, 1)).Rows.Count)

        Dim duplicate = Await Assert.ThrowsExceptionAsync(Of PostgresException)(Function() repo.SaveAsync(Part("K7L0001+A", True)))
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, duplicate.SqlState)
    End Function

End Class
