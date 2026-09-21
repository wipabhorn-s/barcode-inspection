Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.UI
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports Npgsql

<TestClass>
Public Class ReconciliationRuleTests

    <DataTestMethod>
    <DataRow("P1", "", "", "Please select at least work order or carton box no.")>
    <DataRow("P1", "", "abc", "Invalid carton box no.")>
    <DataRow("", "", "5", "Please select product when filtering by carton box no.")>
    Public Sub Filter_RejectsIncompleteInput(product As String, workOrder As String, carton As String, expected As String)
        Dim filter As ReconciliationFilter = Nothing
        Dim problem As String = Nothing

        Assert.IsFalse(ReconciliationFilter.TryCreate(product, workOrder, carton, filter, problem))
        Assert.AreEqual(expected, problem)
    End Sub

    <TestMethod>
    Public Sub Filter_WorkOrderAloneIsEnough()
        Dim filter As ReconciliationFilter = Nothing
        Assert.IsTrue(ReconciliationFilter.TryCreate("", " WO1 ", "", filter, Nothing))
        Assert.AreEqual("WO1", filter.WorkOrder)
        Assert.IsFalse(filter.CartonNo.HasValue)
    End Sub

    <TestMethod>
    Public Sub Filter_CartonWithProduct()
        Dim filter As ReconciliationFilter = Nothing
        Assert.IsTrue(ReconciliationFilter.TryCreate("P1", "", " 7 ", filter, Nothing))
        Assert.AreEqual(7, filter.CartonNo.Value)
        Assert.AreEqual("P1", filter.ProductName)
    End Sub

    <TestMethod>
    Public Sub FindMissing_IgnoresCaseAndSpaces()
        Dim passed = {"A1", "A2", "A3"}.Select(Function(b) New PassedRecord With {.Barcode = b}).ToList()

        Dim missing = Reconciliation.FindMissing(passed, {" a1 ", "A3"})

        CollectionAssert.AreEqual({"A2"}, missing.Select(Function(m) m.Barcode).ToArray())
    End Sub

End Class

''' <summary>Barcode Reconciliation tab against a real PostgreSQL; see <see cref="DatabaseIntegrationTests"/>.</summary>
<TestClass>
<TestCategory("Integration")>
Public Class ReconciliationIntegrationTests

    Private Const Product As String = "IT-RECON"
    Private _db As DbConnectionFactory
    Private _messages As List(Of String)
    Private _passwordAccepted As Boolean
    Private _originalShowMessage As Func(Of String, String, MessageBoxButtons, MessageBoxIcon, MessageBoxDefaultButton, DialogResult)
    Private _originalAskPassword As Func(Of Boolean)

    <TestInitialize>
    Public Sub SetUp()
        Dim connectionString = Environment.GetEnvironmentVariable("BARCODE_INSPECTION_TEST_DB")
        If String.IsNullOrWhiteSpace(connectionString) Then Assert.Inconclusive("BARCODE_INSPECTION_TEST_DB is not set.")
        _db = New DbConnectionFactory(connectionString)

        Execute("INSERT INTO barcode_inspection.product_spec (product_name, barcode_spec, quantity) VALUES (@p, 'N@@@@', 50)")
        Execute("INSERT INTO barcode_inspection.record_barcode_pass (product_name, work_order, barcode, carton_box_no, line_no, operator_id) VALUES
                 (@p, 'IT-RWO', 'N0001', 1, 1, 'OP'), (@p, 'IT-RWO', 'N0002', 1, 1, 'OP'),
                 (@p, 'IT-RWO', 'N0003', 1, 1, 'OP'), (@p, 'IT-RWO', 'N0004', 2, 1, 'OP')")

        _messages = New List(Of String)
        _originalShowMessage = UiKit.ShowMessage
        _originalAskPassword = UiKit.AskAdminPassword
        UiKit.ShowMessage = Function(text, caption, buttons, icon, defaultButton)
                                SyncLock _messages
                                    _messages.Add(text)
                                End SyncLock
                                Return If(buttons = MessageBoxButtons.YesNo, DialogResult.Yes, DialogResult.OK)
                            End Function
        UiKit.AskAdminPassword = Function() _passwordAccepted
    End Sub

    <TestCleanup>
    Public Sub CleanUp()
        If _db Is Nothing Then Return
        UiKit.ShowMessage = _originalShowMessage
        UiKit.AskAdminPassword = _originalAskPassword
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
    Public Async Function Repository_FiltersLikeTheOriginal() As Task
        Dim repo As New ReconciliationRepository(_db)

        Assert.AreEqual(3, (Await repo.GetPassedAsync(New ReconciliationFilter With {.WorkOrder = "IT-RWO", .CartonNo = 1})).Count)
        Assert.AreEqual(4, (Await repo.GetPassedAsync(New ReconciliationFilter With {.WorkOrder = "IT-RWO"})).Count)
        Assert.AreEqual(1, (Await repo.GetPassedAsync(New ReconciliationFilter With {.ProductName = Product, .CartonNo = 2})).Count)
        CollectionAssert.AreEqual({1, 2}, Await repo.GetCartonNumbersAsync("IT-RWO"))
        Assert.AreEqual("IT-RWO", (Await repo.FindPassedAsync("N0003")).WorkOrder)
        Assert.IsNull(Await repo.FindPassedAsync("NOPE"))
    End Function

    Private Shared Function Grid() As DataGridView
        Dim g As New DataGridView With {.AllowUserToAddRows = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect, .MultiSelect = True}
        For i As Integer = 1 To 4
            g.Columns.Add("c" & i, "c" & i)
        Next
        Return g
    End Function

    Private Function BuildView(form As Form) As ReconciliationView
        Dim view As New ReconciliationView With {
            .ProductCombo = New ComboBox(), .BarcodeText = New TextBox(), .AddButton = New Button(),
            .ScannedGrid = Grid(), .ScannedCount = New TextBox(),
            .ScannedClearSelectedMenuItem = New ToolStripMenuItem(), .ScannedClearAllMenuItem = New ToolStripMenuItem(),
            .WorkOrderCombo = New ComboBox(), .CartonCombo = New ComboBox(), .CompareButton = New Button(),
            .MissingGrid = Grid(), .MissingCount = New TextBox(),
            .MissingClearSelectedMenuItem = New ToolStripMenuItem(), .MissingClearAllMenuItem = New ToolStripMenuItem(),
            .MissingDeleteSelectedMenuItem = New ToolStripMenuItem(), .MissingDeleteAllMenuItem = New ToolStripMenuItem(),
            .ResetButton = New Button()}
        For Each control As Control In New Control() {view.ProductCombo, view.BarcodeText, view.AddButton, view.ScannedGrid,
                                                      view.WorkOrderCombo, view.CartonCombo, view.CompareButton, view.MissingGrid}
            form.Controls.Add(control)
        Next
        view.ProductCombo.Items.Add(Product)
        Return view
    End Function

    Private Function PassedCount() As Integer
        Using conn = _db.TryOpenConnection()
            Using cmd As New NpgsqlCommand("SELECT COUNT(*) FROM barcode_inspection.record_barcode_pass WHERE product_name = @p", conn)
                cmd.Parameters.AddWithValue("@p", Product)
                Return Convert.ToInt32(cmd.ExecuteScalar())
            End Using
        End Using
    End Function

    Private Shared Function Barcodes(grid As DataGridView) As String()
        Return grid.Rows.Cast(Of DataGridViewRow)().Select(Function(r) r.Cells(1).Value.ToString()).ToArray()
    End Function

    <TestMethod>
    Public Sub ReconciliationTab_ScanCompareClearDelete()
        RunOnUiThread(
            Sub()
                Using form = OffScreenForm()
                    Dim view = BuildView(form)
                    Dim controller As New ReconciliationController(view, New ReconciliationRepository(_db), New InspectionRepository(_db))
                    form.Show()

                    view.ProductCombo.SelectedIndex = 0
                    WaitUntil(Function() view.WorkOrderCombo.Items.Count = 1, "work orders")
                    view.WorkOrderCombo.SelectedIndex = 0
                    WaitUntil(Function() view.CartonCombo.Items.Count = 2, "cartons")
                    view.CartonCombo.SelectedItem = 1

                    ' Only N0002 is physically in carton 1.
                    view.BarcodeText.Text = "N0002"
                    view.AddButton.PerformClick()
                    WaitUntil(Function() view.ScannedCount.Text = "1", "scan")

                    view.CompareButton.PerformClick()
                    WaitUntil(Function() view.MissingCount.Text <> "", "compare")
                    CollectionAssert.AreEqual({"N0001", "N0003"}, Barcodes(view.MissingGrid))

                    ' Sort descending, select both rows, clear one: the right row must go (old code used grid indexes).
                    view.MissingGrid.Sort(view.MissingGrid.Columns(1), System.ComponentModel.ListSortDirection.Descending)
                    view.MissingGrid.ClearSelection()
                    view.MissingGrid.Rows(0).Selected = True      ' N0003 after sorting
                    view.MissingClearSelectedMenuItem.PerformClick()
                    CollectionAssert.AreEqual({"N0001"}, Barcodes(view.MissingGrid))

                    ' Deleting from the database needs the admin password.
                    view.MissingGrid.Rows(0).Selected = True
                    _passwordAccepted = False
                    view.MissingDeleteSelectedMenuItem.PerformClick()
                    PumpFor(TimeSpan.FromMilliseconds(500))
                    Assert.AreEqual(4, PassedCount(), "wrong password: nothing deleted")

                    _passwordAccepted = True
                    view.MissingDeleteSelectedMenuItem.PerformClick()
                    WaitUntil(Function() view.MissingCount.Text = "0", "delete")
                    Assert.AreEqual(3, PassedCount(), "N0001 deleted")
                    GC.KeepAlive(controller)
                End Using
            End Sub)
    End Sub

End Class
