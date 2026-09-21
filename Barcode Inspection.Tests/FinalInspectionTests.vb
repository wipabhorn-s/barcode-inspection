Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services
Imports Barcode_Inspection.UI
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports Npgsql

<TestClass>
Public Class FolderCheckTests

    <TestMethod>
    Public Sub NoFolderNeeded_WhenFileChecksAreOff()
        Assert.IsFalse(BarcodeInspectionService.FindFolderProblem(New InspectionOptions()).HasValue)
    End Sub

    <TestMethod>
    Public Sub MissingElectricalFolder_IsReportedFirst()
        Dim problem = BarcodeInspectionService.FindFolderProblem(New InspectionOptions With {.CheckElectrical = True, .CheckAvi = True})

        Assert.AreEqual("ELT input folder is missing. Please check the settings.", problem.Value.Message)
        Assert.AreEqual("Path Missing", problem.Value.Caption)
    End Sub

    <TestMethod>
    Public Sub FolderThatDoesNotExist_IsReported()
        Dim problem = BarcodeInspectionService.FindFolderProblem(New InspectionOptions With {.CheckHingeData = True, .HingeFolder = "Z:\no\such\folder"})

        Assert.AreEqual("Hinge folder does not exist. Please verify the folder path.", problem.Value.Message)
        Assert.AreEqual("Invalid Path", problem.Value.Caption)
    End Sub

    <TestMethod>
    Public Sub ExistingFolders_Pass()
        Dim temp = Path.GetTempPath()
        Dim options = New InspectionOptions With {.CheckAvi = True, .AviStation1Folder = temp, .AviStation2Folder = temp}

        Assert.IsFalse(BarcodeInspectionService.FindFolderProblem(options).HasValue)
    End Sub

End Class

''' <summary>Recorder tab against a real PostgreSQL; see <see cref="DatabaseIntegrationTests"/>.</summary>
<TestClass>
<TestCategory("Integration")>
Public Class FinalInspectionIntegrationTests

    Private Const Product As String = "IT-REC"
    Private _db As DbConnectionFactory
    Private _messages As List(Of String)
    Private _originalShowMessage As Func(Of String, String, MessageBoxButtons, MessageBoxIcon, MessageBoxDefaultButton, DialogResult)

    <TestInitialize>
    Public Sub SetUp()
        Dim connectionString = Environment.GetEnvironmentVariable("BARCODE_INSPECTION_TEST_DB")
        If String.IsNullOrWhiteSpace(connectionString) Then Assert.Inconclusive("BARCODE_INSPECTION_TEST_DB is not set.")
        _db = New DbConnectionFactory(connectionString)

        ' Needs a hinge/CMOS scan, no file-based checks, two parts per carton.
        Execute("INSERT INTO barcode_inspection.product_spec (product_name, item_code, barcode_spec, quantity, cmos_check) VALUES (@p, 'ITEM-R', 'R@@@@', 2, TRUE)")
        Execute("INSERT INTO barcode_inspection.record_barcode_pass (product_name, work_order, barcode, carton_box_no, line_no, operator_id)
                 VALUES (@p, 'IT-OLD', 'R9999', 1, 1, 'OP')")

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

    <TestMethod>
    Public Async Function WorkOrders_ComeFromPassedParts() As Task
        CollectionAssert.AreEqual({"IT-OLD"}, Await New InspectionRepository(_db).GetWorkOrdersAsync(Product))
    End Function

    Private Shared Function BuildView(form As Form) As FinalInspectionView
        Dim grid As New DataGridView With {.AllowUserToAddRows = False}
        For i As Integer = 1 To 21
            grid.Columns.Add("c" & i, "c" & i)
        Next
        Dim view As New FinalInspectionView With {
            .ProductCombo = New ComboBox(), .WorkOrderCombo = New ComboBox(), .AddWorkOrderButton = New Button(), .ItemCodeLabel = New Label(),
            .BarcodeFormatText = New TextBox(), .HingeFormatText = New TextBox(), .CapacityText = New TextBox(), .RunningNoText = New TextBox(),
            .LineNoText = New TextBox(), .OperatorText = New TextBox(), .CartonText = New TextBox(), .LoadButton = New Button(),
            .BarcodeText = New TextBox(), .HingeText = New TextBox(), .InspectButton = New Button(), .StatusLabel = New Label(),
            .CountText = New TextBox(), .Grid = grid,
            .Checksum34Check = New CheckBox(), .Checksum36Check = New CheckBox(), .DimensionCheck = New CheckBox(),
            .ElectricalCheck = New CheckBox(), .AppearanceCheck = New CheckBox(), .HingeDataCheck = New CheckBox(),
            .CmosCheck = New CheckBox(), .CompareCheck = New CheckBox(), .AviCheck = New CheckBox(), .RunningNoCheck = New CheckBox(),
            .EltInputFolder = New TextBox(), .EltOutputFolder = New TextBox(), .HingeFolder = New TextBox(),
            .AviStation1Folder = New TextBox(), .AviStation2Folder = New TextBox()}
        For Each control As Control In New Control() {view.ProductCombo, view.WorkOrderCombo, view.LineNoText, view.OperatorText, view.CartonText,
                                                      view.LoadButton, view.BarcodeText, view.HingeText, view.InspectButton, grid}
            form.Controls.Add(control)
        Next
        view.ProductCombo.Items.Add(Product)
        Return view
    End Function

    <TestMethod>
    Public Sub RecorderTab_LoadScanUntilFull()
        RunOnUiThread(
            Sub()
                Using form = OffScreenForm()
                    Dim view = BuildView(form)
                    Dim controller As New FinalInspectionController(view, New InspectionRepository(_db), New ProductSpecRepository(_db))
                    form.Show()

                    view.ProductCombo.SelectedIndex = 0
                    WaitUntil(Function() view.WorkOrderCombo.Items.Count = 1, "product spec and work orders")
                    Assert.AreEqual("ITEM-R", view.ItemCodeLabel.Text)
                    Assert.IsTrue(view.CmosCheck.Checked)

                    view.WorkOrderCombo.Items.Add("IT-NEW")
                    view.WorkOrderCombo.SelectedItem = "IT-NEW"
                    view.LineNoText.Text = "3"
                    view.OperatorText.Text = "OP12345"
                    view.CartonText.Text = "1"

                    ' Loading the carton enables the hinge box because the product needs a CMOS scan.
                    ' (This is the path that hit the RequiresHingeScan infinite loop before.)
                    view.LoadButton.PerformClick()
                    WaitUntil(Function() view.BarcodeText.Enabled, "carton loaded")
                    Assert.AreEqual("0", view.CountText.Text)
                    Assert.IsTrue(view.HingeText.Enabled, "the product needs a CMOS scan, so the hinge box opens with the barcode box")

                    For Each barcode In {"R0001", "R0002"}
                        Dim before = view.CountText.Text
                        view.BarcodeText.Text = barcode
                        view.HingeText.Text = "CMOS-" & barcode
                        view.InspectButton.PerformClick()
                        WaitUntil(Function() view.CountText.Text <> before, "inspect " & barcode)
                        Assert.AreEqual("PASS", view.StatusLabel.Text)
                        WaitUntil(Function() view.InspectButton.Enabled, "scan finished")
                    Next

                    Assert.AreEqual("CMOS-R0002", view.Grid.Rows(1).Cells(2).Value, "hinge is saved with the part")
                    WaitUntil(Function() _messages.Contains("Carton is full. Cannot add more records."), "full warning")
                    Assert.IsFalse(view.BarcodeText.Enabled)
                    GC.KeepAlive(controller)
                End Using
            End Sub)
    End Sub

End Class
