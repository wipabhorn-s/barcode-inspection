Option Strict On

Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services
Imports Npgsql

Namespace UI

    ''' <summary>The controls of one station's scanning tab. The form fills this in once at start-up.</summary>
    Public Class StationInspectionView
        Public Property ProductCombo As ComboBox
        Public Property WorkOrderCombo As ComboBox
        Public Property AddWorkOrderButton As Button
        Public Property ItemCodeLabel As Label
        Public Property BarcodeFormatText As TextBox
        Public Property HingeFormatText As TextBox
        Public Property QuantityText As TextBox
        Public Property RunningNoText As TextBox
        Public Property LineNoText As TextBox
        Public Property OperatorText As TextBox
        Public Property StartButton As Button
        Public Property BarcodeText As TextBox
        Public Property HingeText As TextBox
        Public Property InspectButton As Button
        Public Property StatusLabel As Label
        Public Property CountText As TextBox
        Public Property Grid As DataGridView
        Public Property DeleteMenuItem As ToolStripMenuItem

        ' Read-only copy of the product spec
        Public Property Modulus34Check As CheckBox
        Public Property Modulus36Check As CheckBox
        Public Property DimensionCheck As CheckBox
        Public Property ElectricalCheck As CheckBox
        Public Property AppearanceCheck As CheckBox
        Public Property HingeCheck As CheckBox
        Public Property CmosCheck As CheckBox
        Public Property CompareCheck As CheckBox
        Public Property MachineCheck As CheckBox
        Public Property RunningNoCheck As CheckBox

        Public Property ManualModeRadio As RadioButton
        Public Property PassModeRadio As RadioButton
        Public Property FailModeRadio As RadioButton
    End Class

    ''' <summary>
    ''' Drives one station's scanning tab. The same class runs both the Dimension and the Appearance tab,
    ''' which used to be two copies of the same ~1,000 lines in Form1.
    ''' </summary>
    Public Class StationInspectionController
        Implements IStationPrompts

        Private Shared ReadOnly IdleColor As Color = Color.LemonChiffon
        Private Shared ReadOnly PassColor As Color = Color.PaleGreen
        Private Shared ReadOnly FailColor As Color = Color.LightPink

        Private ReadOnly _info As StationInfo
        Private ReadOnly _view As StationInspectionView
        Private ReadOnly _repository As StationRepository
        Private ReadOnly _products As ProductSpecRepository
        Private ReadOnly _service As StationInspectionService

        Private _isLoadingProduct As Boolean
        Private _isInspecting As Boolean

        Public Sub New(info As StationInfo, view As StationInspectionView, repository As StationRepository, products As ProductSpecRepository)
            _info = info
            _view = view
            _repository = repository
            _products = products
            _service = New StationInspectionService(repository)

            LoadMode()
            WireEvents()
        End Sub

        Private Sub WireEvents()
            With _view
                AddHandler .ProductCombo.SelectedIndexChanged, AddressOf ProductChanged
                AddHandler .ProductCombo.TextChanged, Sub() If .ProductCombo.Text = "" Then ResetProductDetails()
                AddHandler .WorkOrderCombo.SelectedIndexChanged, AddressOf WorkOrderChanged
                AddHandler .WorkOrderCombo.TextChanged, Sub() If .WorkOrderCombo.Text = "" Then ClearGrid()
                AddHandler .AddWorkOrderButton.Click, AddressOf AddWorkOrder
                AddHandler .OperatorText.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub() .StartButton.PerformClick())
                AddHandler .StartButton.Click, AddressOf StartScanning
                AddHandler .BarcodeText.KeyDown, Sub(s, e) UiKit.OnEnter(e, AddressOf BarcodeEntered)
                AddHandler .BarcodeText.TextChanged, Sub() SetStatus("Idle", IdleColor)
                AddHandler .HingeText.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub() .InspectButton.PerformClick())
                AddHandler .InspectButton.Click, AddressOf Inspect
                AddHandler .DeleteMenuItem.Click, AddressOf DeleteSelected
                AddHandler .ManualModeRadio.CheckedChanged, Sub() SaveModeIf(.ManualModeRadio, JudgementMode.Manual)
                AddHandler .PassModeRadio.CheckedChanged, Sub() SaveModeIf(.PassModeRadio, JudgementMode.AlwaysPass)
                AddHandler .FailModeRadio.CheckedChanged, Sub() SaveModeIf(.FailModeRadio, JudgementMode.AlwaysFail)
            End With
        End Sub

#Region "Product and work order"

        Private Async Sub ProductChanged(sender As Object, e As EventArgs)
            If _isLoadingProduct OrElse _view.ProductCombo.SelectedIndex < 0 Then Return

            _isLoadingProduct = True
            Dim productName = _view.ProductCombo.SelectedItem.ToString()
            ResetProductDetails()

            Try
                Dim spec = Await _products.GetAsync(productName)
                Dim workOrders = Await _repository.GetWorkOrdersAsync(productName)

                If spec IsNot Nothing Then ShowProductSpec(spec)
                _view.WorkOrderCombo.Items.Clear()
                _view.WorkOrderCombo.Items.AddRange(workOrders.Cast(Of Object)().ToArray())
            Catch ex As Exception
                UiKit.Error($"Error loading data: {ex.Message}")
            Finally
                _isLoadingProduct = False
            End Try
        End Sub

        Private Sub ShowProductSpec(spec As ProductSpec)
            With _view
                .ItemCodeLabel.Text = spec.ItemCode
                .BarcodeFormatText.Text = spec.BarcodeSpec
                .HingeFormatText.Text = spec.HingeSpec
                .QuantityText.Text = spec.Quantity.ToString()
                .Modulus34Check.Checked = spec.Modulus34
                .Modulus36Check.Checked = spec.Modulus36
                .DimensionCheck.Checked = spec.DimensionCheck
                .ElectricalCheck.Checked = spec.ElectricalCheck
                .AppearanceCheck.Checked = spec.AppearanceCheck
                .HingeCheck.Checked = spec.HingeCheck
                .CmosCheck.Checked = spec.CmosCheck
                .CompareCheck.Checked = spec.CompareCheck
                .MachineCheck.Checked = spec.MachineCheck
                .RunningNoCheck.Checked = spec.RunningCheck
                .RunningNoText.Enabled = spec.RunningCheck
                If Not spec.RunningCheck Then .RunningNoText.Clear()
            End With
        End Sub

        Private Sub ResetProductDetails()
            With _view
                .ItemCodeLabel.Text = String.Empty
                For Each box In { .BarcodeFormatText, .HingeFormatText, .RunningNoText, .QuantityText, .BarcodeText, .HingeText}
                    box.Clear()
                Next
                .RunningNoText.Enabled = False
                .BarcodeText.Enabled = False
                .HingeText.Enabled = False

                .WorkOrderCombo.Text = ""
                .WorkOrderCombo.Items.Clear()
                .WorkOrderCombo.SelectedIndex = -1

                For Each box In { .Modulus34Check, .Modulus36Check, .DimensionCheck, .ElectricalCheck, .AppearanceCheck,
                                  .HingeCheck, .CmosCheck, .CompareCheck, .MachineCheck, .RunningNoCheck}
                    box.Checked = False
                Next
            End With
            ClearGrid()
        End Sub

        Private Async Sub WorkOrderChanged(sender As Object, e As EventArgs)
            ClearGrid()
            If _view.WorkOrderCombo.SelectedIndex < 0 Then Return
            Await LoadRecordsAsync()
        End Sub

        Private Sub AddWorkOrder(sender As Object, e As EventArgs)
            If _view.ProductCombo.SelectedIndex < 0 Then
                UiKit.Warning("Please select a product name before adding a work order.", "Selection Required")
                Return
            End If

            Using form As New WOForm()
                If form.ShowDialog() <> DialogResult.OK Then Return

                If _view.WorkOrderCombo.Items.Contains(form.WorkOrder) Then
                    UiKit.Warning("This WO already exists in the list.", "Duplicate Entry")
                Else
                    _view.WorkOrderCombo.Items.Add(form.WorkOrder)
                    MessageBox.Show("WO added successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
            End Using
        End Sub

#End Region

#Region "Grid"

        Private Sub ClearGrid()
            _view.Grid.DataSource = Nothing
            _view.Grid.Rows.Clear()
            _view.CountText.Clear()
        End Sub

        ''' <summary>Shows the last 100 parts of the work order and the total count.</summary>
        Private Async Function LoadRecordsAsync() As Task
            Try
                Dim productName = _view.ProductCombo.SelectedItem.ToString()
                Dim workOrder = _view.WorkOrderCombo.SelectedItem.ToString()

                Dim records = Await _repository.GetRecentRecordsAsync(productName, workOrder)
                Dim total = Await _repository.CountAsync(productName, workOrder)

                BindGrid(records)
                _view.CountText.Text = total.ToString()
                SelectLastRow()
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
            Catch ex As Exception
                UiKit.Error($"Error loading data: {ex.Message}")
            End Try
        End Function

        Private Sub BindGrid(records As DataTable)
            Dim columns = {"part_no", "barcode", "barcode_hinge", "work_order", "line_no",
                           "inspection_date", "inspection_time", _info.ResultColumn, "remark"}

            _view.Grid.AutoGenerateColumns = False
            _view.Grid.DataSource = records
            For i As Integer = 0 To columns.Length - 1
                _view.Grid.Columns(i).DataPropertyName = columns(i)
            Next
        End Sub

        Private Sub SelectLastRow()
            Dim grid = _view.Grid
            If grid.Rows.Count = 0 Then Return

            Dim lastIndex = grid.Rows.Count - 1
            grid.ClearSelection()
            grid.Rows(lastIndex).Selected = True
            grid.FirstDisplayedScrollingRowIndex = lastIndex
            grid.CurrentCell = grid.Rows(lastIndex).Cells(0)
        End Sub

        ''' <summary>Appends the part just saved, or reloads the grid if that is not possible.</summary>
        Private Async Function AddSavedRowAsync(inspection As InspectionData) As Task
            Dim gridTable = TryCast(_view.Grid.DataSource, DataTable)

            Try
                Dim newRow = Await _repository.GetLatestRecordAsync(inspection.Barcode)
                If gridTable IsNot Nothing AndAlso newRow.Rows.Count > 0 Then
                    gridTable.ImportRow(newRow.Rows(0))
                    gridTable.AcceptChanges()
                    _view.CountText.Text = (Await _repository.CountAsync(inspection.ProductName, inspection.WorkOrder)).ToString()
                    SelectLastRow()
                    Return
                End If
            Catch ex As Exception
                Debug.WriteLine($"{_info.DisplayName} AddSavedRow Error: {ex.Message}")
            End Try

            Await LoadRecordsAsync()
        End Function

        Private Async Sub DeleteSelected(sender As Object, e As EventArgs)
            If _view.Grid.SelectedRows.Count = 0 Then Return
            If Not UiKit.ConfirmAdminDelete("Are you sure you want to delete the selected record(s)?") Then Return

            Dim barcodes = _view.Grid.SelectedRows.Cast(Of DataGridViewRow)().
                Where(Function(r) r.Cells(1).Value IsNot Nothing).
                Select(Function(r) r.Cells(1).Value.ToString()).
                ToList()

            Try
                Dim deleted = Await _repository.DeleteByBarcodesAsync(barcodes)
                Await LoadRecordsAsync()
                MessageBox.Show($"{deleted} record(s) deleted successfully.", "Deleted", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex As Exception
                UiKit.Error($"Delete error: {ex.Message}")
            End Try
        End Sub

#End Region

#Region "Scanning"

        Private Function RequiresHingeScan() As Boolean
            Return _view.CompareCheck.Checked OrElse _view.CmosCheck.Checked OrElse _view.HingeCheck.Checked
        End Function

        Private Sub StartScanning(sender As Object, e As EventArgs)
            If Not ValidateSetup() Then Return

            _view.BarcodeText.Enabled = True
            _view.BarcodeText.Focus()
            _view.BarcodeText.SelectAll()
            _view.HingeText.Enabled = RequiresHingeScan()
        End Sub

        Private Sub BarcodeEntered()
            If RequiresHingeScan() Then
                _view.HingeText.Enabled = True
                _view.HingeText.Focus()
                _view.HingeText.SelectAll()
            Else
                _view.InspectButton.PerformClick()
            End If
        End Sub

        Private Function ValidateSetup() As Boolean
            With _view
                If .ProductCombo.SelectedIndex < 0 Then Return UiKit.Invalid("Please select product name.", "Input Required")
                If .WorkOrderCombo.SelectedIndex < 0 Then Return UiKit.Invalid("Please select work order.", "Input Required")

                Dim lineNo As Integer
                If Not Integer.TryParse(.LineNoText.Text.Trim(), lineNo) OrElse lineNo <= 0 Then
                    Return UiKit.Invalid("Please enter a valid line no. (number greater than 0).", "Invalid Input", .LineNoText)
                End If

                If String.IsNullOrWhiteSpace(.OperatorText.Text) Then Return UiKit.Invalid("Please enter operator id.", "Input Required", .OperatorText)

                If .RunningNoCheck.Checked AndAlso String.IsNullOrWhiteSpace(.RunningNoText.Text) Then
                    Return UiKit.Invalid("This product requires Running No. range." & Environment.NewLine & "Please enter Running No. before loading.",
                                "Running No. Required", .RunningNoText)
                End If
            End With
            Return True
        End Function

        Private Function ValidateScan() As Boolean
            If String.IsNullOrWhiteSpace(_view.BarcodeText.Text) Then Return UiKit.Invalid("Please enter barcode.", "Input Required", _view.BarcodeText)

            If RequiresHingeScan() AndAlso String.IsNullOrWhiteSpace(_view.HingeText.Text) Then
                Return UiKit.Invalid("Please enter hinge or CMOS.", "Input Required", _view.HingeText)
            End If
            Return True
        End Function

        Private Async Sub Inspect(sender As Object, e As EventArgs)
            If _isInspecting Then Return ' ignore a second scan while the first is still being saved
            If Not ValidateSetup() OrElse Not ValidateScan() Then Return

            _isInspecting = True
            _view.InspectButton.Enabled = False
            Try
                Await InspectAsync(_view.BarcodeText.Text.Trim(), _view.HingeText.Text.Trim())
            Finally
                _isInspecting = False
                _view.InspectButton.Enabled = True
                _view.BarcodeText.SelectAll()
                _view.BarcodeText.Focus()
            End Try
        End Sub

        Private Async Function InspectAsync(barcode As String, hinge As String) As Task
            Message.ResetErrorFlag()

            Try
                Dim outcome = Await _service.InspectAsync(BuildRequest(barcode, hinge), BuildOptions(), CurrentMode(), Me)

                For Each failure In outcome.Failures
                    Message.ResultFailCase(failure.Subject, failure.Message, CInt(failure.Priority))
                Next

                Select Case outcome.Status
                    Case StationScanStatus.Rejected
                        SetStatus("FAIL", FailColor)
                        Message.ShowFirstError()

                    Case StationScanStatus.Cancelled
                        SetStatus("Idle", IdleColor)

                    Case StationScanStatus.Judged
                        Await _service.SaveAsync(outcome)
                        If outcome.Inspection.IsPass Then
                            SetStatus("PASS", PassColor)
                        Else
                            SetStatus("FAIL", FailColor)
                        End If
                        Await AddSavedRowAsync(outcome.Inspection)
                End Select

            Catch ex As PostgresException When ex.SqlState = PostgresErrorCodes.UniqueViolation
                Message.ResultFailCase(barcode, "Database constraint error, please contact admin", CInt(FailurePriority.Critical))
                SetStatus("FAIL", FailColor)
                Message.ShowFirstError()
            Catch ex As Exception
                UiKit.Error($"Database Error: {ex.Message}")
            End Try
        End Function

        Private Function BuildRequest(barcode As String, hinge As String) As InspectionRequest
            Return New InspectionRequest With {
                .ProductName = _view.ProductCombo.Text,
                .WorkOrder = _view.WorkOrderCombo.Text,
                .Barcode = barcode,
                .Hinge = hinge,
                .LineNo = Integer.Parse(_view.LineNoText.Text.Trim()),
                .OperatorId = _view.OperatorText.Text.Trim()
            }
        End Function

        Private Function BuildOptions() As InspectionOptions
            Return New InspectionOptions With {
                .BarcodeTemplate = _view.BarcodeFormatText.Text,
                .HingeTemplate = _view.HingeFormatText.Text,
                .CheckChecksum34 = _view.Modulus34Check.Checked,
                .CheckChecksum36 = _view.Modulus36Check.Checked,
                .CheckRunningNo = _view.RunningNoCheck.Checked,
                .RunningNoRange = _view.RunningNoText.Text
            }
        End Function

        Private Sub SetStatus(text As String, color As Color)
            _view.StatusLabel.Text = text
            _view.StatusLabel.BackColor = color
        End Sub

#End Region

#Region "Operator prompts (IStationPrompts)"

        Private Function AskRunningNoDecision(rangeText As String, barcode As String) As RunningNoDecision Implements IStationPrompts.AskRunningNoDecision
            Using dlg As New Dialog1()
                dlg.SetInfo(rangeText, barcode)
                dlg.ShowDialog()
                Select Case dlg.SelectedAction
                    Case "Rework" : Return RunningNoDecision.Rework
                    Case "Reject" : Return RunningNoDecision.Reject
                    Case Else : Return RunningNoDecision.None
                End Select
            End Using
        End Function

        Private Function AskFailReason() As String Implements IStationPrompts.AskFailReason
            Do
                Dim reason = Microsoft.VisualBasic.Interaction.InputBox("Please enter reason for FAIL:" & Environment.NewLine & "(Cannot be empty)", "Fail Reason")
                If Not String.IsNullOrWhiteSpace(reason) Then Return reason

                If MessageBox.Show("Reason is required. Cancel this scan?", "Required", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
                    Return Nothing
                End If
            Loop
        End Function

        Private Function AskJudgement(barcode As String) As StationJudgement Implements IStationPrompts.AskJudgement
            Using dlg As New Dialog2()
                dlg.SetBarcode(barcode)
                If dlg.ShowDialog() <> DialogResult.OK Then Return Nothing
                Return New StationJudgement With {.IsPass = dlg.SelectedResult, .Remark = dlg.Remark}
            End Using
        End Function

#End Region

#Region "Judgement mode"

        Private Function CurrentMode() As JudgementMode
            Return StationInfo.ParseMode(CStr(My.Settings(_info.ModeSettingName)))
        End Function

        Private Sub LoadMode()
            Select Case CurrentMode()
                Case JudgementMode.AlwaysPass : _view.PassModeRadio.Checked = True
                Case JudgementMode.AlwaysFail : _view.FailModeRadio.Checked = True
                Case Else : _view.ManualModeRadio.Checked = True
            End Select
        End Sub

        Private Sub SaveModeIf(radio As RadioButton, mode As JudgementMode)
            If Not radio.Checked Then Return
            My.Settings(_info.ModeSettingName) = CInt(mode).ToString()
            My.Settings.Save()
        End Sub

#End Region


    End Class

End Namespace
