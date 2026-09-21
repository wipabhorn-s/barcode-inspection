Imports System.Threading
Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Services
Imports Barcode_Inspection.UI
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports Npgsql

<TestClass>
Public Class CombinedCartonServiceTests

    Private Class FakeRepository
        Implements ICombinedCartonRepository

        Public ReadOnly Passed As New Dictionary(Of String, PassedPart)
        Public ReadOnly Combined As New List(Of CombinedEntry)
        ''' <summary>Simulates another station combining the barcode between our check and our insert.</summary>
        Public LoseRace As Boolean

        Public Function FindPassedPartAsync(barcode As String) As Task(Of PassedPart) Implements ICombinedCartonRepository.FindPassedPartAsync
            Dim part As PassedPart = Nothing
            Passed.TryGetValue(barcode, part)
            Return Task.FromResult(part)
        End Function

        Public Function IsCombinedAsync(barcode As String) As Task(Of Boolean) Implements ICombinedCartonRepository.IsCombinedAsync
            Return Task.FromResult(Combined.Any(Function(c) c.Barcode = barcode))
        End Function

        Public Function CountInCartonAsync(productName As String, cartonNo As Integer) As Task(Of Integer) Implements ICombinedCartonRepository.CountInCartonAsync
            Return Task.FromResult(Combined.Where(Function(c) c.ProductName = productName AndAlso c.CartonNo = cartonNo).Count())
        End Function

        Public Function InsertAsync(entry As CombinedEntry) As Task(Of Boolean) Implements ICombinedCartonRepository.InsertAsync
            If LoseRace Then Return Task.FromResult(False)
            Combined.Add(entry)
            Return Task.FromResult(True)
        End Function
    End Class

    Private _repository As FakeRepository
    Private _service As CombinedCartonService

    <TestInitialize>
    Public Sub SetUp()
        _repository = New FakeRepository()
        _repository.Passed("A1") = New PassedPart With {.ProductName = "P1", .WorkOrder = "WO1"}
        _repository.Passed("A2") = New PassedPart With {.ProductName = "P1", .WorkOrder = "WO2"}
        _repository.Passed("B1") = New PassedPart With {.ProductName = "P2", .WorkOrder = "WO9"}
        _service = New CombinedCartonService(_repository)
    End Sub

    Private Function Add(barcode As String, Optional capacity As Integer = 10) As CombineResult
        Return _service.AddAsync(New CombineRequest With {
            .ProductName = "P1", .CartonNo = 1, .Capacity = capacity, .Barcode = barcode, .OperatorId = "OP"}).Result
    End Function

    <TestMethod>
    Public Sub PassedPart_IsAddedWithItsOwnWorkOrder()
        Assert.IsTrue(Add("A1").Success)
        Assert.IsTrue(Add("A2").Success)

        CollectionAssert.AreEqual({"WO1", "WO2"}, _repository.Combined.Select(Function(c) c.WorkOrder).ToArray(),
                                  "one combined carton can hold several work orders")
    End Sub

    <DataTestMethod>
    <DataRow("ZZ", "Barcode has not been scanned yet")>
    <DataRow("B1", "Barcode belongs to 'P2'")>
    Public Sub WrongPart_IsRefused(barcode As String, expected As String)
        Dim result = Add(barcode)

        Assert.IsFalse(result.Success)
        Assert.AreEqual(expected, result.FailureMessage)
        Assert.AreEqual(0, _repository.Combined.Count)
    End Sub

    <TestMethod>
    Public Sub SamePartTwice_IsRefused()
        Add("A1")
        Assert.AreEqual("Barcode has already been combined", Add("A1").FailureMessage)
    End Sub

    <TestMethod>
    Public Sub FullCarton_IsRefused()
        Assert.IsTrue(Add("A1", capacity:=1).Success)
        Assert.AreEqual("Combined carton is full", Add("A2", capacity:=1).FailureMessage)
    End Sub

    <TestMethod>
    Public Sub LosingTheRaceToAnotherStation_IsReportedAsAlreadyCombined()
        _repository.LoseRace = True
        Assert.AreEqual("Barcode has already been combined", Add("A1").FailureMessage)
    End Sub

End Class

''' <summary>Combined carton SQL and tab against a real PostgreSQL; see <see cref="DatabaseIntegrationTests"/>.</summary>
<TestClass>
<TestCategory("Integration")>
Public Class CombinedCartonIntegrationTests

    Private Const Product As String = "IT-COMB"
    Private _db As DbConnectionFactory
    Private _messages As List(Of String)
    Private _originalShowMessage As Func(Of String, String, MessageBoxButtons, MessageBoxIcon, MessageBoxDefaultButton, DialogResult)

    <TestInitialize>
    Public Sub SetUp()
        Dim connectionString = Environment.GetEnvironmentVariable("BARCODE_INSPECTION_TEST_DB")
        If String.IsNullOrWhiteSpace(connectionString) Then Assert.Inconclusive("BARCODE_INSPECTION_TEST_DB is not set.")
        _db = New DbConnectionFactory(connectionString)

        Execute("INSERT INTO barcode_inspection.product_spec (product_name, item_code, barcode_spec, quantity) VALUES (@p, 'ITEM-C', 'C@@@@', 2)")
        Execute("INSERT INTO barcode_inspection.record_barcode_pass (product_name, work_order, barcode, carton_box_no, line_no, operator_id) VALUES
                 (@p, 'IT-WO1', 'C0001', 1, 1, 'OP'), (@p, 'IT-WO2', 'C0002', 1, 1, 'OP'), (@p, 'IT-WO2', 'C0003', 1, 1, 'OP')")

        _messages = New List(Of String)
        _originalShowMessage = UiKit.ShowMessage
        UiKit.ShowMessage = Function(text, caption, buttons, icon, defaultButton)
                                SyncLock _messages
                                    _messages.Add(text)
                                End SyncLock
                                Return DialogResult.OK
                            End Function
    End Sub

    <TestCleanup>
    Public Sub CleanUp()
        If _db Is Nothing Then Return
        UiKit.ShowMessage = _originalShowMessage
        Execute("DELETE FROM barcode_inspection.record_combined_carton WHERE product_name = @p")
        Execute("DELETE FROM barcode_inspection.record_barcode_pass WHERE product_name = @p")
        Execute("DELETE FROM barcode_inspection.pass_part_counter WHERE work_order LIKE 'IT-%'")
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

    <TestMethod>
    Public Async Function Repository_RealSql() As Task
        Dim repo As New CombinedCartonRepository(_db)
        Dim entry = New CombinedEntry With {.CartonNo = 5, .ProductName = Product, .WorkOrder = "IT-WO1", .Barcode = "C0001", .OperatorId = "OP"}

        Assert.AreEqual("IT-WO1", (Await repo.FindPassedPartAsync("C0001")).WorkOrder)
        Assert.IsNull(Await repo.FindPassedPartAsync("NOPE"))
        Assert.IsTrue(Await repo.InsertAsync(entry))
        Assert.IsFalse(Await repo.InsertAsync(entry), "the unique barcode constraint turns a double insert into False")
        Assert.IsTrue(Await repo.IsCombinedAsync("C0001"))
        Assert.AreEqual(1, Await repo.CountInCartonAsync(Product, 5))
        Assert.AreEqual(1, (Await repo.GetCartonAsync(Product, 5)).Rows.Count)
        Assert.AreEqual("C0001", (Await repo.GetLatestAsync("C0001")).Rows(0)("barcode"))
    End Function

    <TestMethod>
    Public Sub CombinedTab_ScanUntilCartonIsFull()
        RunOnUiThread(
            Sub()
                Using form = OffScreenForm()
                    Dim grid As New DataGridView With {.AllowUserToAddRows = False}
                    For i As Integer = 1 To 6
                        grid.Columns.Add("c" & i, "c" & i)
                    Next
                    Dim view As New CombinedCartonView With {
                        .ProductCombo = New ComboBox(), .ItemCodeLabel = New Label(), .BarcodeFormatText = New TextBox(), .CapacityText = New TextBox(),
                        .OperatorText = New TextBox(), .CartonText = New TextBox(), .LoadButton = New Button(), .BarcodeText = New TextBox(),
                        .AddButton = New Button(), .StatusLabel = New Label(), .CountText = New TextBox(), .Grid = grid}
                    For Each control As Control In New Control() {view.ProductCombo, view.OperatorText, view.CartonText, view.BarcodeText, view.AddButton, view.LoadButton, grid, view.CountText}
                        form.Controls.Add(control)
                    Next
                    view.ProductCombo.Items.Add(Product)

                    Dim controller As New CombinedCartonController(view, New CombinedCartonRepository(_db), New ProductSpecRepository(_db))
                    form.Show()

                    view.ProductCombo.SelectedIndex = 0
                    WaitUntil(Function() view.CapacityText.Text = "2", "product spec")
                    Assert.AreEqual("ITEM-C", view.ItemCodeLabel.Text)

                    ' A second badge scan replaces the first one instead of being appended.
                    view.OperatorText.Text = "1234567"
                    view.OperatorText.Text &= "7654321"
                    Assert.AreEqual("7654321", view.OperatorText.Text)

                    view.CartonText.Text = "8"
                    view.LoadButton.PerformClick()
                    WaitUntil(Function() view.CountText.Text = "0", "empty carton")
                    WaitUntil(Function() view.BarcodeText.Enabled, "barcode box enabled for an empty carton")

                    For Each barcode In {"C0001", "C0002"}
                        Dim before = view.CountText.Text
                        view.BarcodeText.Text = barcode
                        view.AddButton.PerformClick()
                        WaitUntil(Function() view.CountText.Text <> before, "add " & barcode)
                        Assert.AreEqual("PASS", view.StatusLabel.Text)
                        ' The next scan is ignored until this one has finished (protects against a double Enter).
                        WaitUntil(Function() view.AddButton.Enabled, "scan finished")
                    Next

                    ' Two parts from two work orders fill the carton (capacity 2): scanning stops.
                    WaitUntil(Function() _messages.Contains("Combined carton is full. Cannot add more records."), "full warning")
                    Assert.IsFalse(view.BarcodeText.Enabled)
                    Assert.AreEqual(2, grid.Rows.Count)
                    GC.KeepAlive(controller)
                End Using
            End Sub)
    End Sub

End Class
