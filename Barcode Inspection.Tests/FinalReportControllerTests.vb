Imports System.Threading
Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.UI
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports Npgsql

''' <summary>
''' Drives <see cref="FinalReportController"/> through real WinForms controls (built in code, not the designer)
''' against a real database, the same way a user would: pick a product, tick filters, click Search.
''' </summary>
<TestClass>
<TestCategory("Integration")>
Public Class FinalReportControllerTests

    Private Const Product As String = "IT-UI"

    Private _db As DbConnectionFactory

    <TestInitialize>
    Public Sub SetUp()
        Dim connectionString = Environment.GetEnvironmentVariable("BARCODE_INSPECTION_TEST_DB")
        If String.IsNullOrWhiteSpace(connectionString) Then Assert.Inconclusive("BARCODE_INSPECTION_TEST_DB is not set.")
        _db = New DbConnectionFactory(connectionString)

        Execute("INSERT INTO barcode_inspection.product_spec (product_name, barcode_spec, quantity) VALUES (@p, 'K7L@@@@', 50)")
        Execute("INSERT INTO barcode_inspection.record_barcode_pass (product_name, work_order, barcode, carton_box_no, line_no, operator_id) VALUES
                 (@p, 'IT-WO1', 'U0001', 1, 1, 'OP'), (@p, 'IT-WO1', 'U0002', 1, 1, 'OP'), (@p, 'IT-WO2', 'U0003', 2, 1, 'OP')")
        Execute("INSERT INTO barcode_inspection.record_barcode_fail (product_name, work_order, barcode, carton_box_no, line_no, operator_id) VALUES
                 (@p, 'IT-WO1', 'U0009', 1, 1, 'OP')")
    End Sub

    <TestCleanup>
    Public Sub CleanUp()
        If _db Is Nothing Then Return
        Execute("DELETE FROM barcode_inspection.record_barcode_pass WHERE product_name = @p")
        Execute("DELETE FROM barcode_inspection.record_barcode_fail WHERE product_name = @p")
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

    Private Shared Sub Search(view As FinalReportView, barcode As String)
        view.BarcodeText.Text = barcode
        view.SearchButton.PerformClick()
    End Sub

    Private Shared Function Grid(columnCount As Integer) As DataGridView
        Dim g As New DataGridView With {.AllowUserToAddRows = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect}
        For i As Integer = 1 To columnCount
            g.Columns.Add("c" & i, "Column " & i)
        Next
        Return g
    End Function

    Private Function BuildScreen(form As Form) As FinalReportView
        Dim view As New FinalReportView With {
            .ProductCombo = New ComboBox(), .WorkOrderFilter = New CheckBox(), .WorkOrderCombo = New ComboBox(),
            .CartonFilter = New CheckBox(), .CartonText = New TextBox(), .DateFilter = New CheckBox(),
            .DateFrom = New DateTimePicker(), .DateTo = New DateTimePicker(),
            .BarcodeFilter = New CheckBox(), .BarcodeText = New TextBox(), .HingeFilter = New CheckBox(), .HingeText = New TextBox(),
            .DefectFilter = New CheckBox(), .CombinedFilter = New CheckBox(), .CombinedCombo = New ComboBox(),
            .SearchButton = New Button(), .ClearButton = New Button(), .ExportButton = New Button(),
            .PassGrid = Grid(28), .PassCount = New TextBox(), .FailGrid = Grid(24), .FailCount = New TextBox(),
            .PassClearSelectedMenuItem = New ToolStripMenuItem(), .PassClearAllMenuItem = New ToolStripMenuItem(),
            .PassDeleteMenuItem = New ToolStripMenuItem(), .DeleteCombinedMenuItem = New ToolStripMenuItem(),
            .FailClearAllMenuItem = New ToolStripMenuItem(), .FailDeleteMenuItem = New ToolStripMenuItem()}

        For Each control As Control In New Control() {view.ProductCombo, view.WorkOrderCombo, view.CartonText, view.BarcodeText, view.HingeText,
                                        view.CombinedCombo, view.PassGrid, view.PassCount, view.FailGrid, view.FailCount,
                                        view.WorkOrderFilter, view.DefectFilter, view.BarcodeFilter, view.SearchButton, view.ClearButton}
            form.Controls.Add(control)
        Next
        view.ProductCombo.Items.Add(Product)
        Return view
    End Function

    <TestMethod>
    Public Sub SearchByWorkOrder_ThenAddDefects_ThenClear()
        RunOnUiThread(
            Sub()
                Using form = OffScreenForm()
                    Dim view = BuildScreen(form)
                    Dim controller As New FinalReportController(view, New FinalReportRepository(_db))
                    form.Show()

                    ' Choosing the product loads its work orders.
                    view.ProductCombo.SelectedIndex = 0
                    WaitUntil(Function() view.WorkOrderCombo.Items.Count = 2, "work orders")

                    ' Ticking the filter enables the work-order box.
                    view.WorkOrderFilter.Checked = True
                    Assert.IsTrue(view.WorkOrderCombo.Enabled)
                    view.WorkOrderCombo.SelectedItem = "IT-WO1"

                    view.SearchButton.PerformClick()
                    WaitUntil(Function() view.PassCount.Text <> "", "pass search")
                    Assert.AreEqual("2", view.PassCount.Text)
                    Assert.AreEqual("U0001", view.PassGrid.Rows(0).Cells(2).Value)
                    Assert.AreEqual("", view.FailCount.Text, "Defect not ticked: fail grid stays empty")

                    view.DefectFilter.Checked = True
                    view.SearchButton.PerformClick()
                    WaitUntil(Function() view.FailCount.Text <> "", "fail search")
                    Assert.AreEqual("2", view.PassCount.Text)
                    Assert.AreEqual("1", view.FailCount.Text)

                    ' Clear empties both grids and both counts (the old code left the fail count behind).
                    view.ClearButton.PerformClick()
                    Assert.AreEqual("", view.PassCount.Text)
                    Assert.AreEqual("", view.FailCount.Text)
                    Assert.AreEqual(0, view.PassGrid.Rows.Count)
                    Assert.IsFalse(view.WorkOrderFilter.Checked)
                    Assert.IsFalse(view.WorkOrderCombo.Enabled)
                    GC.KeepAlive(controller)
                End Using
            End Sub)
    End Sub

    <TestMethod>
    Public Sub BarcodeOnlySearches_AddUpInTheGrid()
        RunOnUiThread(
            Sub()
                Using form = OffScreenForm()
                    Dim view = BuildScreen(form)
                    Dim controller As New FinalReportController(view, New FinalReportRepository(_db))
                    form.Show()

                    view.BarcodeFilter.Checked = True
                    Search(view, "U0001")
                    WaitUntil(Function() view.PassCount.Text = "1", "first barcode")
                    Search(view, "U0003")
                    WaitUntil(Function() view.PassCount.Text = "2", "second barcode")
                    Search(view, "U0001")
                    PumpFor(TimeSpan.FromSeconds(2))

                    ' Scanning U0001 twice must not show it twice.
                    Assert.AreEqual("2", view.PassCount.Text)
                    GC.KeepAlive(controller)
                End Using
            End Sub)
    End Sub

End Class
