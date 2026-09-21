Imports System.Threading
Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services
Imports Barcode_Inspection.UI
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports Npgsql

<TestClass>
Public Class ProductSpecValidationTests

    <DataTestMethod>
    <DataRow("", "I1", "K7L@@@@", "50", ProductField.ProductName)>
    <DataRow("P1", " ", "K7L@@@@", "50", ProductField.ItemCode)>
    <DataRow("P1", "I1", "", "50", ProductField.BarcodeSpec)>
    <DataRow("P1", "I1", "K7L@@@@", "", ProductField.Quantity)>
    <DataRow("P1", "I1", "K7L@@@@", "0", ProductField.Quantity)>
    <DataRow("P1", "I1", "K7L@@@@", "abc", ProductField.Quantity)>
    <DataRow("P1", "I1", "K7L@@@@", " 50 ", ProductField.None)>
    Public Sub Check_ReportsFirstBadField(name As String, itemCode As String, barcodeSpec As String, quantity As String, expected As ProductField)
        Assert.AreEqual(expected, ProductSpecValidation.Check(name, itemCode, barcodeSpec, quantity).Field)
    End Sub


End Class

''' <summary>Product repository and Setting tab against a real PostgreSQL; see <see cref="DatabaseIntegrationTests"/>.</summary>
<TestClass>
<TestCategory("Integration")>
Public Class ProductSettingsIntegrationTests

    Private _db As DbConnectionFactory
    Private _repo As ProductSpecRepository
    Private _messages As List(Of String)
    Private _originalShowMessage As Func(Of String, String, MessageBoxButtons, MessageBoxIcon, MessageBoxDefaultButton, DialogResult)

    <TestInitialize>
    Public Sub SetUp()
        Dim connectionString = Environment.GetEnvironmentVariable("BARCODE_INSPECTION_TEST_DB")
        If String.IsNullOrWhiteSpace(connectionString) Then Assert.Inconclusive("BARCODE_INSPECTION_TEST_DB is not set.")
        _db = New DbConnectionFactory(connectionString)
        _repo = New ProductSpecRepository(_db)
        AdminPasswordService.Current = New AdminPasswordService(New AppSettingRepository(_db))

        ' Record message boxes instead of showing them, and answer "Yes" to every question.
        _messages = New List(Of String)
        _originalShowMessage = UiKit.ShowMessage
        UiKit.ShowMessage = Function(text, caption, buttons, icon, defaultButton)
                             SyncLock _messages
                                 _messages.Add(text)
                             End SyncLock
                             Return If(buttons = MessageBoxButtons.YesNo, DialogResult.Yes, DialogResult.OK)
                         End Function
    End Sub

    <TestCleanup>
    Public Sub CleanUp()
        If _db Is Nothing Then Return
        UiKit.ShowMessage = _originalShowMessage
        Execute("DELETE FROM barcode_inspection.record_barcode_pass WHERE product_name LIKE 'IT-SET%'")
        Execute("DELETE FROM barcode_inspection.pass_part_counter WHERE work_order LIKE 'IT-%'")
        Execute("DELETE FROM barcode_inspection.product_spec WHERE product_name LIKE 'IT-SET%'")
    End Sub

    Private Sub Execute(sql As String)
        Using conn = _db.TryOpenConnection()
            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Private Shared Function Spec(name As String, Optional quantity As Integer = 50) As ProductSpec
        Return New ProductSpec With {.ProductName = name, .ItemCode = "I-" & name, .BarcodeSpec = "K7L@@@@", .Quantity = quantity, .RunningCheck = True}
    End Function

    <TestMethod>
    Public Async Function Repository_InsertUpdateDelete() As Task
        Await _repo.InsertAsync(Spec("IT-SET-A"))

        Dim duplicate = Await Assert.ThrowsExceptionAsync(Of PostgresException)(Function() _repo.InsertAsync(Spec("IT-SET-A")))
        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, duplicate.SqlState)

        Assert.AreEqual(1, Await _repo.UpdateAsync("IT-SET-A", Spec("IT-SET-B", quantity:=80)))
        Assert.IsNull(Await _repo.GetAsync("IT-SET-A"))
        Assert.AreEqual(80, (Await _repo.GetAsync("IT-SET-B")).Quantity)
        Assert.IsTrue((Await _repo.GetAllAsync()).Any(Function(p) p.ProductName = "IT-SET-B"))

        Assert.IsTrue(Await _repo.DeleteAsync("IT-SET-B"))
        Assert.IsFalse(Await _repo.DeleteAsync("IT-SET-B"), "already gone")
    End Function

    <TestMethod>
    Public Async Function Repository_ProductInUseCannotBeDeleted() As Task
        Await _repo.InsertAsync(Spec("IT-SET-USED"))
        Execute("INSERT INTO barcode_inspection.record_barcode_pass (product_name, work_order, barcode, carton_box_no, line_no, operator_id)
                 VALUES ('IT-SET-USED', 'IT-WO', 'SET0001', 1, 1, 'OP')")

        Dim ex = Await Assert.ThrowsExceptionAsync(Of PostgresException)(Function() _repo.DeleteAsync("IT-SET-USED"))
        Assert.AreEqual(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState)
    End Function

#Region "Setting tab through real WinForms controls"

    Private Function LastMessage() As String
        SyncLock _messages
            Return If(_messages.Count = 0, Nothing, _messages(_messages.Count - 1))
        End SyncLock
    End Function

    Private Shared Function BuildView(form As Form) As SettingsView
        Dim grid As New DataGridView With {.AllowUserToAddRows = False, .SelectionMode = DataGridViewSelectionMode.FullRowSelect, .MultiSelect = False, .ReadOnly = True}
        For i As Integer = 1 To 15
            If i <= 5 Then grid.Columns.Add(New DataGridViewTextBoxColumn()) Else grid.Columns.Add(New DataGridViewCheckBoxColumn())
        Next

        Dim view As New SettingsView With {
            .PasswordLabel = New Label(), .PasswordText = New TextBox(), .UnlockButton = New Button(),
            .EltInputFolder = New TextBox(), .EltOutputFolder = New TextBox(), .HingeFolder = New TextBox(),
            .AviStation1Folder = New TextBox(), .AviStation2Folder = New TextBox(),
            .EltInputBrowse = New Button(), .EltOutputBrowse = New Button(), .HingeBrowse = New Button(),
            .AviStation1Browse = New Button(), .AviStation2Browse = New Button(),
            .ProductGrid = grid, .ProductNameText = New TextBox(), .ItemCodeText = New TextBox(), .BarcodeSpecText = New TextBox(),
            .HingeSpecText = New TextBox(), .QuantityText = New TextBox(),
            .Modulus34Check = New CheckBox(), .Modulus36Check = New CheckBox(), .DimensionCheck = New CheckBox(),
            .ElectricalCheck = New CheckBox(), .AppearanceCheck = New CheckBox(), .HingeCheck = New CheckBox(),
            .CmosCheck = New CheckBox(), .CompareCheck = New CheckBox(), .MachineCheck = New CheckBox(), .RunningCheck = New CheckBox(),
            .NewButton = New Button With {.Text = "New"}, .EditButton = New Button With {.Text = "Edit"},
            .DeleteButton = New Button With {.Text = "Delete"}, .CancelButton = New Button With {.Text = "Cancel"},
            .ChangePasswordButton = New Button()}

        For Each control As Control In New Control() {view.PasswordText, view.UnlockButton, grid, view.ProductNameText, view.ItemCodeText,
                                                      view.BarcodeSpecText, view.HingeSpecText, view.QuantityText, view.RunningCheck,
                                                      view.NewButton, view.EditButton, view.DeleteButton, view.CancelButton}
            form.Controls.Add(control)
        Next
        Return view
    End Function

    Private Shared Function GridNames(view As SettingsView) As List(Of String)
        Return view.ProductGrid.Rows.Cast(Of DataGridViewRow)().Select(Function(r) r.Cells(0).Value.ToString()).ToList()
    End Function

    <TestMethod>
    Public Sub SettingTab_LockAddEditDelete()
        RunOnUiThread(
            Sub()
                Using form = OffScreenForm()
                    Dim view = BuildView(form)
                    Dim controller As New SettingsController(view, _repo)
                    Dim published As IReadOnlyList(Of String) = Nothing
                    AddHandler controller.ProductsLoaded, Sub(names) published = names
                    form.Show()

                    Dim loading = controller.LoadProductsAsync()
                    WaitUntil(Function() loading.IsCompleted, "initial load")
                    Assert.IsNotNull(published, "other tabs are told about the product list")

                    ' Locked until the admin password is entered.
                    controller.Lock()
                    Assert.IsFalse(view.NewButton.Enabled)
                    view.PasswordText.Text = "wrong-password"
                    view.UnlockButton.PerformClick()
                    WaitUntil(Function() LastMessage() = "Incorrect password. Please try again.", "wrong password refused")
                    Assert.IsFalse(view.NewButton.Enabled)
                    view.PasswordText.Text = "12345678"
                    view.UnlockButton.PerformClick()
                    WaitUntil(Function() view.NewButton.Enabled, "unlock")
                    Assert.IsFalse(view.PasswordText.Visible)

                    ' New: the form opens empty; saving without a name is refused.
                    view.NewButton.PerformClick()
                    Assert.AreEqual("Save", view.NewButton.Text)
                    Assert.IsFalse(view.EditButton.Enabled)
                    Assert.IsTrue(view.ProductNameText.Enabled)
                    view.NewButton.PerformClick()
                    Assert.AreEqual("Please enter product name.", LastMessage())

                    view.ProductNameText.Text = "IT-SET-UI"
                    view.ItemCodeText.Text = "ITEM"
                    view.BarcodeSpecText.Text = "K7L@@@@"
                    view.QuantityText.Text = "40"
                    view.RunningCheck.Checked = True
                    view.NewButton.PerformClick()
                    WaitUntil(Function() LastMessage() = "Product saved successfully!", "save")
                    Assert.AreEqual("New", view.NewButton.Text)
                    Assert.IsFalse(view.ProductNameText.Enabled)
                    CollectionAssert.Contains(published.ToList(), "IT-SET-UI")
                    Assert.AreEqual("IT-SET-UI", view.ProductGrid.CurrentRow.Cells(0).Value, "the saved product is selected")

                    ' Edit: rename it.
                    view.EditButton.PerformClick()
                    Assert.AreEqual("Save", view.EditButton.Text)
                    view.ProductNameText.Text = "IT-SET-UI2"
                    view.EditButton.PerformClick()
                    WaitUntil(Function() LastMessage() = "Product updated successfully!", "update")
                    CollectionAssert.Contains(GridNames(view), "IT-SET-UI2")
                    CollectionAssert.DoesNotContain(GridNames(view), "IT-SET-UI")
                    Assert.AreEqual("40", view.QuantityText.Text)
                    Assert.IsTrue(view.RunningCheck.Checked)

                    ' Delete (the test answers "Yes").
                    view.DeleteButton.PerformClick()
                    WaitUntil(Function() LastMessage() = "Product deleted successfully!", "delete")
                    CollectionAssert.DoesNotContain(GridNames(view), "IT-SET-UI2")
                    GC.KeepAlive(controller)
                End Using
            End Sub)
    End Sub

#End Region

End Class
