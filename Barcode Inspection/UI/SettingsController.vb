Option Strict On

Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services
Imports Microsoft.WindowsAPICodePack.Dialogs
Imports Npgsql

Namespace UI

    ''' <summary>The controls of the Setting tab.</summary>
    Public Class SettingsView
        ' Admin lock
        Public Property PasswordLabel As Label
        Public Property PasswordText As TextBox
        Public Property UnlockButton As Button

        ' Folders for the final inspection checks
        Public Property EltInputFolder As TextBox
        Public Property EltOutputFolder As TextBox
        Public Property HingeFolder As TextBox
        Public Property AviStation1Folder As TextBox
        Public Property AviStation2Folder As TextBox
        Public Property EltInputBrowse As Button
        Public Property EltOutputBrowse As Button
        Public Property HingeBrowse As Button
        Public Property AviStation1Browse As Button
        Public Property AviStation2Browse As Button

        ' Product list and form
        Public Property ProductGrid As DataGridView
        Public Property ProductNameText As TextBox
        Public Property ItemCodeText As TextBox
        Public Property BarcodeSpecText As TextBox
        Public Property HingeSpecText As TextBox
        Public Property QuantityText As TextBox
        Public Property Modulus34Check As CheckBox
        Public Property Modulus36Check As CheckBox
        Public Property DimensionCheck As CheckBox
        Public Property ElectricalCheck As CheckBox
        Public Property AppearanceCheck As CheckBox
        Public Property HingeCheck As CheckBox
        Public Property CmosCheck As CheckBox
        Public Property CompareCheck As CheckBox
        Public Property MachineCheck As CheckBox
        Public Property RunningCheck As CheckBox
        Public Property NewButton As Button
        Public Property EditButton As Button
        Public Property DeleteButton As Button
        Public Property CancelButton As Button

        Public Property ChangePasswordButton As Button
    End Class

    ''' <summary>
    ''' Drives the Setting tab: the admin lock, the input folders, and the product list
    ''' (add / edit / delete). Raises <see cref="ProductsLoaded"/> so the other tabs can refresh their product lists.
    ''' </summary>
    Public Class SettingsController

        Private Enum EditMode
            Viewing
            Adding
            Editing
        End Enum

        ' Grid column order as laid out in the designer.
        Private Shared ReadOnly GridColumns As String() = {
            NameOf(ProductSpec.ProductName), NameOf(ProductSpec.ItemCode), NameOf(ProductSpec.BarcodeSpec), NameOf(ProductSpec.HingeSpec),
            NameOf(ProductSpec.Quantity), NameOf(ProductSpec.Modulus34), NameOf(ProductSpec.Modulus36), NameOf(ProductSpec.DimensionCheck),
            NameOf(ProductSpec.ElectricalCheck), NameOf(ProductSpec.AppearanceCheck), NameOf(ProductSpec.HingeCheck),
            NameOf(ProductSpec.CmosCheck), NameOf(ProductSpec.CompareCheck), NameOf(ProductSpec.MachineCheck), NameOf(ProductSpec.RunningCheck)}

        Private ReadOnly _view As SettingsView
        Private ReadOnly _repository As ProductSpecRepository
        Private _mode As EditMode = EditMode.Viewing
        Private _products As New List(Of ProductSpec)

        ''' <summary>Raised with every product name, in order, after the list is (re)loaded.</summary>
        Public Event ProductsLoaded(productNames As IReadOnlyList(Of String))

        Public Sub New(view As SettingsView, repository As ProductSpecRepository)
            _view = view
            _repository = repository

            ShowFolders()
            WireEvents()
        End Sub

        Private Sub WireEvents()
            With _view
                AddHandler .UnlockButton.Click, AddressOf Unlock
                AddHandler .PasswordText.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub() .UnlockButton.PerformClick())

                BindFolder(.EltInputBrowse, .EltInputFolder, "Select Input Folder", NameOf(My.MySettings.input_elt_folder))
                BindFolder(.EltOutputBrowse, .EltOutputFolder, "Select Output Folder", NameOf(My.MySettings.output_elt_folder))
                BindFolder(.HingeBrowse, .HingeFolder, "Select Hinge Folder", NameOf(My.MySettings.input_hinge_folder))
                BindFolder(.AviStation1Browse, .AviStation1Folder, "Select AVI ST1 Folder", NameOf(My.MySettings.avi_station1))
                BindFolder(.AviStation2Browse, .AviStation2Folder, "Select AVI ST2 Folder", NameOf(My.MySettings.avi_station2))

                AddHandler .ProductGrid.SelectionChanged, Sub() If _mode = EditMode.Viewing Then ShowSelectedProduct()
                AddHandler .NewButton.Click, AddressOf NewOrSave
                AddHandler .EditButton.Click, AddressOf EditOrSave
                AddHandler .DeleteButton.Click, AddressOf Delete
                AddHandler .CancelButton.Click, Sub() If _mode <> EditMode.Viewing Then CancelEdit()
                AddHandler .ChangePasswordButton.Click, AddressOf ChangePassword
            End With
        End Sub

#Region "Admin lock"

        Private ReadOnly Property LockedControls As Control()
            Get
                With _view
                    Return New Control() { .NewButton, .EditButton, .DeleteButton, .CancelButton, .ChangePasswordButton,
                                           .EltInputBrowse, .EltOutputBrowse, .HingeBrowse, .AviStation1Browse, .AviStation2Browse,
                                           .EltInputFolder, .EltOutputFolder, .HingeFolder, .AviStation1Folder, .AviStation2Folder}
                End With
            End Get
        End Property

        ''' <summary>Called whenever the tab is opened: everything is read-only until the admin password is entered.</summary>
        Public Sub Lock()
            For Each control In LockedControls
                control.Enabled = False
            Next
            SetPasswordPromptVisible(True)
            _view.PasswordText.Clear()
            _view.PasswordText.Focus()
        End Sub

        Private Async Sub Unlock(sender As Object, e As EventArgs)
            _view.UnlockButton.Enabled = False
            Dim correct = Await UiKit.CheckAdminPasswordAsync(_view.PasswordText.Text)
            _view.UnlockButton.Enabled = True

            If Not correct Then
                _view.PasswordText.Clear()
                _view.PasswordText.Focus()
                Return
            End If

            For Each control In LockedControls
                control.Enabled = True
            Next
            SetPasswordPromptVisible(False)
            SetMode(_mode)
            _view.EltInputFolder.Select(_view.EltInputFolder.TextLength, 0)
            _view.EltInputFolder.Focus()
        End Sub

        Private Sub SetPasswordPromptVisible(visible As Boolean)
            _view.PasswordLabel.Visible = visible
            _view.PasswordText.Visible = visible
            _view.UnlockButton.Visible = visible
        End Sub

        Private Sub ChangePassword(sender As Object, e As EventArgs)
            Using dialog As New ChangePasswordForm(AddressOf SubmitPasswordChangeAsync)
                If dialog.ShowDialog(_view.ProductGrid.FindForm()) = DialogResult.OK Then
                    UiKit.Info("The admin password has been changed. Use the new password from now on, on every PC.", "Password Changed")
                End If
            End Using
        End Sub

        Private Async Function SubmitPasswordChangeAsync(current As String, newPassword As String, confirmation As String) As Task(Of String)
            Try
                Return Await AdminPasswordService.Current.ChangeAsync(current, newPassword, confirmation)
            Catch ex As PostgresException When ex.SqlState = PostgresErrorCodes.UndefinedTable
                Return AdminPasswordService.NotConfiguredMessage
            Catch ex As Exception
                Return $"Cannot save the new password: {ex.Message}"
            End Try
        End Function

#End Region

#Region "Folders"

        ''' <summary>Shows saved folders that still exist.</summary>
        Private Sub ShowFolders()
            With _view
                ShowFolder(.EltInputFolder, My.Settings.input_elt_folder)
                ShowFolder(.EltOutputFolder, My.Settings.output_elt_folder)
                ShowFolder(.HingeFolder, My.Settings.input_hinge_folder)
                ShowFolder(.AviStation1Folder, My.Settings.avi_station1)
                ShowFolder(.AviStation2Folder, My.Settings.avi_station2)
            End With
        End Sub

        Private Shared Sub ShowFolder(box As TextBox, path As String)
            box.Text = If(IO.Directory.Exists(path), path, "")
        End Sub

        ''' <summary>The "..." button picks a folder, saves it to the user setting, and shows it.</summary>
        Private Shared Sub BindFolder(browse As Button, box As TextBox, title As String, settingName As String)
            AddHandler browse.Click,
                Sub()
                    Dim folder = PickFolder(title)
                    If folder Is Nothing Then Return
                    My.Settings(settingName) = folder
                    My.Settings.Save()
                    box.Text = folder
                End Sub
        End Sub

        Private Shared Function PickFolder(title As String) As String
            Try
                Using dialog As New CommonOpenFileDialog()
                    dialog.IsFolderPicker = True
                    dialog.Title = title
                    dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    dialog.EnsurePathExists = True
                    If dialog.ShowDialog() = CommonFileDialogResult.Ok Then Return dialog.FileName
                End Using
            Catch ex As Exception
                UiKit.Error($"Error: {ex.Message}")
            End Try
            Return Nothing
        End Function

#End Region

#Region "Product list"

        ''' <summary>Reloads the product list and selects <paramref name="selectName"/> (or the row at <paramref name="selectIndex"/>).</summary>
        Public Async Function LoadProductsAsync(Optional selectName As String = Nothing, Optional selectIndex As Integer = 0) As Task
            Try
                _products = Await _repository.GetAllAsync()
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database. Please check network.", "Connection Error")
                Return
            Catch ex As Exception
                UiKit.Error($"Error loading products: {ex.Message}")
                Return
            End Try

            Dim grid = _view.ProductGrid
            grid.AutoGenerateColumns = False
            For i As Integer = 0 To GridColumns.Length - 1
                grid.Columns(i).DataPropertyName = GridColumns(i)
            Next
            grid.DataSource = ToTable(_products)

            Dim index = If(selectName Is Nothing, selectIndex, _products.FindIndex(Function(p) p.ProductName = selectName))
            If _products.Count = 0 Then
                ClearForm()
            Else
                SelectRow(Math.Max(0, Math.Min(index, _products.Count - 1)))
            End If

            RaiseEvent ProductsLoaded(_products.Select(Function(p) p.ProductName).ToList())
        End Function

        Private Function SelectedProduct() As ProductSpec
            Dim row = TryCast(_view.ProductGrid.CurrentRow?.DataBoundItem, DataRowView)
            If row Is Nothing Then Return Nothing
            Dim name = row(NameOf(ProductSpec.ProductName)).ToString()
            Return _products.FirstOrDefault(Function(p) p.ProductName = name)
        End Function

        ''' <summary>A DataTable rather than the list itself, so clicking a column header still sorts the grid.</summary>
        Private Shared Function ToTable(products As IEnumerable(Of ProductSpec)) As DataTable
            Dim table As New DataTable()
            For Each column In GridColumns
                table.Columns.Add(column, GetType(ProductSpec).GetProperty(column).PropertyType)
            Next
            For Each product In products
                table.Rows.Add(GridColumns.Select(Function(c) GetType(ProductSpec).GetProperty(c).GetValue(product)).ToArray())
            Next
            Return table
        End Function

        Private Sub SelectRow(index As Integer)
            Dim grid = _view.ProductGrid
            grid.ClearSelection()
            grid.Rows(index).Selected = True
            grid.CurrentCell = grid.Rows(index).Cells(0)
            ShowSelectedProduct()
        End Sub

        Private Sub ShowSelectedProduct()
            Dim product = SelectedProduct()
            If product IsNot Nothing Then ShowInForm(product)
        End Sub

#End Region

#Region "Product form"

        Private ReadOnly Property FormChecks As CheckBox()
            Get
                With _view
                    Return { .Modulus34Check, .Modulus36Check, .DimensionCheck, .ElectricalCheck, .AppearanceCheck,
                             .HingeCheck, .CmosCheck, .CompareCheck, .MachineCheck, .RunningCheck}
                End With
            End Get
        End Property

        Private ReadOnly Property FormTexts As TextBox()
            Get
                With _view
                    Return { .ProductNameText, .ItemCodeText, .BarcodeSpecText, .HingeSpecText, .QuantityText}
                End With
            End Get
        End Property

        Private Sub ShowInForm(product As ProductSpec)
            With _view
                .ProductNameText.Text = product.ProductName
                .ItemCodeText.Text = product.ItemCode
                .BarcodeSpecText.Text = product.BarcodeSpec
                .HingeSpecText.Text = product.HingeSpec
                .QuantityText.Text = product.Quantity.ToString()
                .Modulus34Check.Checked = product.Modulus34
                .Modulus36Check.Checked = product.Modulus36
                .DimensionCheck.Checked = product.DimensionCheck
                .ElectricalCheck.Checked = product.ElectricalCheck
                .AppearanceCheck.Checked = product.AppearanceCheck
                .HingeCheck.Checked = product.HingeCheck
                .CmosCheck.Checked = product.CmosCheck
                .CompareCheck.Checked = product.CompareCheck
                .MachineCheck.Checked = product.MachineCheck
                .RunningCheck.Checked = product.RunningCheck
            End With
        End Sub

        Private Function ReadForm() As ProductSpec
            Dim v = _view
            Return New ProductSpec With {
                .ProductName = v.ProductNameText.Text.Trim(),
                .ItemCode = v.ItemCodeText.Text.Trim(),
                .BarcodeSpec = v.BarcodeSpecText.Text.Trim(),
                .HingeSpec = v.HingeSpecText.Text.Trim(),
                .Quantity = Integer.Parse(v.QuantityText.Text.Trim()),
                .Modulus34 = v.Modulus34Check.Checked,
                .Modulus36 = v.Modulus36Check.Checked,
                .DimensionCheck = v.DimensionCheck.Checked,
                .ElectricalCheck = v.ElectricalCheck.Checked,
                .AppearanceCheck = v.AppearanceCheck.Checked,
                .HingeCheck = v.HingeCheck.Checked,
                .CmosCheck = v.CmosCheck.Checked,
                .CompareCheck = v.CompareCheck.Checked,
                .MachineCheck = v.MachineCheck.Checked,
                .RunningCheck = v.RunningCheck.Checked
            }
        End Function

        Private Sub ClearForm()
            For Each box In FormTexts
                box.Clear()
            Next
            For Each check In FormChecks
                check.Checked = False
            Next
        End Sub

        Private Function ValidateForm() As Boolean
            With _view
                Dim result = ProductSpecValidation.Check(.ProductNameText.Text, .ItemCodeText.Text, .BarcodeSpecText.Text, .QuantityText.Text)
                If result.IsValid Then Return True

                Dim box As TextBox
                Select Case result.Field
                    Case ProductField.ProductName : box = .ProductNameText
                    Case ProductField.ItemCode : box = .ItemCodeText
                    Case ProductField.BarcodeSpec : box = .BarcodeSpecText
                    Case Else : box = .QuantityText
                End Select
                Dim caption = If(result.Message.StartsWith("Quantity must"), "Invalid Input", "Input Required")
                Return UiKit.Invalid(result.Message, caption, box)
            End With
        End Function

        ''' <summary>Switches between browsing the list and filling in the form. Buttons show "Save" while editing.</summary>
        Private Sub SetMode(mode As EditMode)
            _mode = mode
            Dim editing = mode <> EditMode.Viewing

            With _view
                .ProductGrid.Enabled = Not editing
                For Each box In FormTexts
                    box.Enabled = editing
                Next
                For Each check In FormChecks
                    check.Enabled = editing
                Next

                .NewButton.Text = If(mode = EditMode.Adding, "Save", "New")
                .EditButton.Text = If(mode = EditMode.Editing, "Save", "Edit")
                .NewButton.Enabled = mode <> EditMode.Editing
                .EditButton.Enabled = mode <> EditMode.Adding
                .DeleteButton.Enabled = Not editing
            End With
        End Sub

        Private Sub CancelEdit()
            SetMode(EditMode.Viewing)
            If SelectedProduct() IsNot Nothing Then ShowSelectedProduct() Else ClearForm()
        End Sub

#End Region

#Region "Add / edit / delete"

        Private Async Sub NewOrSave(sender As Object, e As EventArgs)
            If _mode = EditMode.Viewing Then
                ClearForm()
                SetMode(EditMode.Adding)
                _view.ProductNameText.Focus()
                Return
            End If

            If Not ValidateForm() Then Return
            Dim product = ReadForm()

            If Await TrySaveAsync(Function() _repository.InsertAsync(product), product.ProductName) Then
                SetMode(EditMode.Viewing)
                Await LoadProductsAsync(selectName:=product.ProductName)
                UiKit.Info("Product saved successfully!", "Success")
            End If
        End Sub

        Private Async Sub EditOrSave(sender As Object, e As EventArgs)
            Dim current = SelectedProduct()
            If current Is Nothing Then Return

            If _mode = EditMode.Viewing Then
                SetMode(EditMode.Editing)
                _view.ProductNameText.Select(_view.ProductNameText.TextLength, 0)
                _view.ProductNameText.Focus()
                Return
            End If

            If Not ValidateForm() Then Return
            Dim product = ReadForm()

            If Await TrySaveAsync(Function() _repository.UpdateAsync(current.ProductName, product), product.ProductName) Then
                SetMode(EditMode.Viewing)
                Await LoadProductsAsync(selectName:=product.ProductName)
                UiKit.Info("Product updated successfully!", "Success")
            End If
        End Sub

        ''' <summary>Runs an insert or update and explains a duplicate name to the user.</summary>
        Private Async Function TrySaveAsync(save As Func(Of Task), productName As String) As Task(Of Boolean)
            Try
                Await save()
                Return True
            Catch ex As PostgresException When ex.SqlState = PostgresErrorCodes.UniqueViolation
                UiKit.Invalid($"Product '{productName}' already exists.", "Duplicate Product", _view.ProductNameText)
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database. Please check network.", "Connection Error")
            Catch ex As Exception
                UiKit.Error($"Database error: {ex.Message}")
            End Try
            Return False
        End Function

        Private Async Sub Delete(sender As Object, e As EventArgs)
            Dim current = SelectedProduct()
            If current Is Nothing Then Return

            If Not UiKit.Confirm("Are you sure you want to delete this product?", "Confirm Delete") Then Return

            Dim index = _view.ProductGrid.CurrentRow.Index
            Try
                If Not Await _repository.DeleteAsync(current.ProductName) Then
                    UiKit.Warning("Product not found.", "Delete Failed")
                    Return
                End If
            Catch ex As PostgresException When ex.SqlState = PostgresErrorCodes.ForeignKeyViolation
                UiKit.Warning("Cannot delete this product because it is already used in inspection records.", "Delete Restricted")
                Return
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database. Please check network.", "Connection Error")
                Return
            Catch ex As Exception
                UiKit.Error($"Database error: {ex.Message}")
                Return
            End Try

            Await LoadProductsAsync(selectIndex:=index)
            UiKit.Info("Product deleted successfully!", "Deleted")
        End Sub

#End Region

    End Class

End Namespace
