Option Strict On

Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services
Imports Npgsql

Namespace UI

    ''' <summary>The controls of the Recorder (final inspection) tab.</summary>
    Public Class FinalInspectionView
        Public Property ProductCombo As ComboBox
        Public Property WorkOrderCombo As ComboBox
        Public Property AddWorkOrderButton As Button
        Public Property ItemCodeLabel As Label
        Public Property BarcodeFormatText As TextBox
        Public Property HingeFormatText As TextBox
        Public Property CapacityText As TextBox
        Public Property RunningNoText As TextBox
        Public Property LineNoText As TextBox
        Public Property OperatorText As TextBox
        Public Property CartonText As TextBox
        Public Property LoadButton As Button
        Public Property BarcodeText As TextBox
        Public Property HingeText As TextBox
        Public Property InspectButton As Button
        Public Property StatusLabel As Label
        Public Property CountText As TextBox
        Public Property Grid As DataGridView

        ' Read-only copy of the product spec: which checks run
        Public Property Checksum34Check As CheckBox
        Public Property Checksum36Check As CheckBox
        Public Property DimensionCheck As CheckBox
        Public Property ElectricalCheck As CheckBox
        Public Property AppearanceCheck As CheckBox
        Public Property HingeDataCheck As CheckBox
        Public Property CmosCheck As CheckBox
        Public Property CompareCheck As CheckBox
        Public Property AviCheck As CheckBox
        Public Property RunningNoCheck As CheckBox

        ' Folders chosen on the Setting tab
        Public Property EltInputFolder As TextBox
        Public Property EltOutputFolder As TextBox
        Public Property HingeFolder As TextBox
        Public Property AviStation1Folder As TextBox
        Public Property AviStation2Folder As TextBox
    End Class

    ''' <summary>
    ''' Drives the Recorder tab: choose product / work order / carton, then scan parts through
    ''' <see cref="BarcodeInspectionService"/> and show PASS or FAIL.
    ''' </summary>
    Public Class FinalInspectionController

        Private Shared ReadOnly GridColumns As String() = {
            "part_no", "barcode", "barcode_hinge", "work_order", "carton_box_no", "line_no", "inspection_date", "inspection_time",
            "dimension_result", "electrical_result", "appearance_result", "hinge_max_15", "hinge_avg_15", "hinge_max_120",
            "hinge_avg_120", "hinge_angle", "hinge_vendor", "hinge_date", "mc_1_result", "mc_2_result", "remark"}

        Private ReadOnly _view As FinalInspectionView
        Private ReadOnly _repository As InspectionRepository
        Private ReadOnly _products As ProductSpecRepository
        Private ReadOnly _service As BarcodeInspectionService
        Private _isLoadingProduct As Boolean
        Private _isInspecting As Boolean

        Public Sub New(view As FinalInspectionView, repository As InspectionRepository, products As ProductSpecRepository)
            _view = view
            _repository = repository
            _products = products
            _service = New BarcodeInspectionService(repository)

            WireEvents()
        End Sub

        Private Sub WireEvents()
            With _view
                AddHandler .ProductCombo.SelectedIndexChanged, AddressOf ProductChanged
                AddHandler .ProductCombo.TextChanged, Sub() If .ProductCombo.Text = "" Then ResetProductDetails()
                AddHandler .WorkOrderCombo.SelectedIndexChanged, AddressOf WorkOrderChanged
                AddHandler .AddWorkOrderButton.Click, AddressOf AddWorkOrder

                UiKit.KeepLastCharacters(.OperatorText, 7)
                AddHandler .OperatorText.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub() .CartonText.Focus())
                AddHandler .CartonText.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub() .LoadButton.PerformClick())
                AddHandler .LoadButton.Click, AddressOf LoadCarton

                AddHandler .BarcodeText.KeyDown, Sub(s, e) UiKit.OnEnter(e, AddressOf BarcodeEntered)
                AddHandler .BarcodeText.TextChanged, Sub() SetStatus("Idle", Color.LemonChiffon)
                AddHandler .HingeText.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub() .InspectButton.PerformClick())
                AddHandler .InspectButton.Click, AddressOf Inspect
            End With
        End Sub

        ''' <summary>Hinge / CMOS must be scanned when the product checks hinge data, the CMOS, or compares them.</summary>
        Private Function RequiresHingeScan() As Boolean
            Return _view.HingeDataCheck.Checked OrElse _view.CmosCheck.Checked OrElse _view.CompareCheck.Checked
        End Function

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
                .CapacityText.Text = spec.Quantity.ToString()
                .Checksum34Check.Checked = spec.Modulus34
                .Checksum36Check.Checked = spec.Modulus36
                .DimensionCheck.Checked = spec.DimensionCheck
                .ElectricalCheck.Checked = spec.ElectricalCheck
                .AppearanceCheck.Checked = spec.AppearanceCheck
                .HingeDataCheck.Checked = spec.HingeCheck
                .CmosCheck.Checked = spec.CmosCheck
                .CompareCheck.Checked = spec.CompareCheck
                .AviCheck.Checked = spec.MachineCheck
                .RunningNoCheck.Checked = spec.RunningCheck
                .RunningNoText.Enabled = spec.RunningCheck
                If Not spec.RunningCheck Then .RunningNoText.Clear()
            End With
        End Sub

        Private Sub ResetProductDetails()
            With _view
                .ItemCodeLabel.Text = String.Empty
                For Each box As TextBox In { .BarcodeFormatText, .HingeFormatText, .CapacityText, .CartonText, .BarcodeText, .HingeText, .CountText, .RunningNoText}
                    box.Clear()
                Next
                .BarcodeText.Enabled = False
                .HingeText.Enabled = False
                .RunningNoText.Enabled = False

                .WorkOrderCombo.Text = ""
                .WorkOrderCombo.Items.Clear()
                .WorkOrderCombo.SelectedIndex = -1

                For Each check As CheckBox In { .Checksum34Check, .Checksum36Check, .DimensionCheck, .ElectricalCheck, .AppearanceCheck,
                                                .HingeDataCheck, .CmosCheck, .CompareCheck, .AviCheck, .RunningNoCheck}
                    check.Checked = False
                Next

                .Grid.DataSource = Nothing
                .Grid.Rows.Clear()
            End With
        End Sub

        ''' <summary>A new work order means a new carton: stop scanning until it is loaded.</summary>
        Private Sub WorkOrderChanged(sender As Object, e As EventArgs)
            With _view
                .BarcodeText.Clear()
                .HingeText.Clear()
                .BarcodeText.Enabled = False
                .HingeText.Enabled = False
                .Grid.DataSource = Nothing
            End With
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
                    UiKit.Info("WO added successfully!", "Success")
                End If
            End Using
        End Sub

#End Region

#Region "Carton"

        Private Function ValidateSetup() As Boolean
            With _view
                If .ProductCombo.SelectedIndex < 0 Then Return UiKit.Invalid("Please select product name.")
                If .WorkOrderCombo.SelectedIndex < 0 Then Return UiKit.Invalid("Please select work order.")

                Dim number As Integer
                If Not Integer.TryParse(.LineNoText.Text.Trim(), number) OrElse number <= 0 Then
                    Return UiKit.Invalid("Please enter a valid line no. (number greater than 0).", "Invalid Input", .LineNoText)
                End If
                If String.IsNullOrWhiteSpace(.OperatorText.Text) Then Return UiKit.Invalid("Please enter operator id.", focus:= .OperatorText)
                If Not Integer.TryParse(.CartonText.Text.Trim(), number) OrElse number <= 0 Then
                    Return UiKit.Invalid("Please enter a valid carton box no. (number greater than 0).", "Invalid Input", .CartonText)
                End If
            End With

            Dim folderProblem = BarcodeInspectionService.FindFolderProblem(ReadOptions())
            If folderProblem IsNot Nothing Then
                UiKit.Warning(folderProblem.Value.Message, folderProblem.Value.Caption)
                Return False
            End If
            Return True
        End Function

        Private Async Sub LoadCarton(sender As Object, e As EventArgs)
            If Not ValidateSetup() Then Return

            If _view.RunningNoCheck.Checked AndAlso String.IsNullOrWhiteSpace(_view.RunningNoText.Text) Then
                UiKit.Invalid("This product requires Running No. range." & Environment.NewLine & "Please enter Running No. before loading.",
                              "Running No. Required", _view.RunningNoText)
                Return
            End If

            If Await LoadGridAsync() Then CheckCartonFull(showWarning:=True)
        End Sub

        Private Async Function LoadGridAsync() As Task(Of Boolean)
            Try
                Dim rows = Await _repository.GetCartonRecordsAsync(ProductName, WorkOrder, CartonNo)
                With _view.Grid
                    .AutoGenerateColumns = False
                    For i As Integer = 0 To GridColumns.Length - 1
                        .Columns(i).DataPropertyName = GridColumns(i)
                    Next
                    .DataSource = rows
                End With
                _view.CountText.Text = rows.Rows.Count.ToString()
                SelectLastRow()
                Return True
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
            Catch ex As Exception
                UiKit.Error($"Error loading data: {ex.Message}")
            End Try
            Return False
        End Function

        Private Sub SelectLastRow()
            Dim grid = _view.Grid
            If grid.Rows.Count = 0 Then Return
            Dim lastIndex = grid.Rows.Count - 1
            grid.ClearSelection()
            grid.Rows(lastIndex).Selected = True
            grid.FirstDisplayedScrollingRowIndex = lastIndex
            grid.CurrentCell = grid.Rows(lastIndex).Cells(0)
        End Sub

        ''' <summary>Disables scanning when the carton is full; otherwise gets the barcode box ready.</summary>
        Private Sub CheckCartonFull(showWarning As Boolean)
            Dim capacity, count As Integer
            If Not Integer.TryParse(_view.CapacityText.Text, capacity) OrElse Not Integer.TryParse(_view.CountText.Text, count) Then Return

            If count >= capacity Then
                _view.BarcodeText.Enabled = False
                _view.HingeText.Enabled = False
                If showWarning Then UiKit.Invalid("Carton is full. Cannot add more records.", "Carton Full", _view.CartonText)
                Return
            End If

            _view.BarcodeText.Enabled = True
            _view.HingeText.Enabled = RequiresHingeScan()
            _view.BarcodeText.Focus()
            _view.BarcodeText.SelectAll()
        End Sub

        ''' <summary>Appends the part just saved, or reloads the carton if that is not possible.</summary>
        Private Async Function AddSavedRowAsync(barcode As String) As Task
            Dim gridTable = TryCast(_view.Grid.DataSource, DataTable)
            Try
                Dim newRow = Await _repository.GetLatestPassRecordAsync(barcode)
                If gridTable IsNot Nothing AndAlso newRow.Rows.Count > 0 Then
                    gridTable.ImportRow(newRow.Rows(0))
                    gridTable.AcceptChanges()
                    _view.CountText.Text = gridTable.Rows.Count.ToString()
                    SelectLastRow()
                    Return
                End If
            Catch ex As Exception
                Debug.WriteLine($"AddSavedRow Error: {ex.Message}")
            End Try

            Await LoadGridAsync()
        End Function

        Private ReadOnly Property ProductName As String
            Get
                Return _view.ProductCombo.SelectedItem.ToString()
            End Get
        End Property

        Private ReadOnly Property WorkOrder As String
            Get
                Return _view.WorkOrderCombo.SelectedItem.ToString()
            End Get
        End Property

        Private ReadOnly Property CartonNo As Integer
            Get
                Return Integer.Parse(_view.CartonText.Text.Trim())
            End Get
        End Property

#End Region

#Region "Scanning"

        Private Sub BarcodeEntered()
            If RequiresHingeScan() Then
                _view.HingeText.Enabled = True
                _view.HingeText.Focus()
                _view.HingeText.SelectAll()
            Else
                _view.InspectButton.PerformClick()
            End If
        End Sub

        Private Function ValidateScan() As Boolean
            If String.IsNullOrWhiteSpace(_view.BarcodeText.Text) Then Return UiKit.Invalid("Please enter barcode.", focus:=_view.BarcodeText)
            If RequiresHingeScan() AndAlso String.IsNullOrWhiteSpace(_view.HingeText.Text) Then
                Return UiKit.Invalid("Please enter hinge or CMOS.", focus:=_view.HingeText)
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
            End Try
        End Sub

        Private Async Function InspectAsync(barcode As String, hinge As String) As Task
            Message.ResetErrorFlag()
            Dim options = ReadOptions()

            Try
                Dim outcome = Await _service.InspectAsync(BuildRequest(barcode, hinge), options, AddressOf AskRunningNoDecision)
                For Each failure In outcome.Failures
                    Message.ResultFailCase(failure.Subject, failure.Message, CInt(failure.Priority))
                Next

                Await _service.SaveAsync(outcome, options)

                If outcome.Inspection.IsPass Then
                    SetStatus("PASS", Color.PaleGreen)
                    Await AddSavedRowAsync(barcode)
                Else
                    SetStatus("FAIL", Color.LightCoral)
                End If

                Message.ShowFirstError()
                CheckCartonFull(showWarning:=True)

            Catch ex As PostgresException When ex.SqlState = PostgresErrorCodes.UniqueViolation
                Message.ResultFailCase(barcode, "Database constraint error, please contact admin", CInt(FailurePriority.Critical))
                SetStatus("FAIL", Color.LightCoral)
                Message.ShowFirstError()
            Catch ex As Exception
                UiKit.Error($"Database Error: {ex.Message}")
            End Try
        End Function

        Private Function BuildRequest(barcode As String, hinge As String) As InspectionRequest
            Dim v = _view
            Return New InspectionRequest With {
                .ProductName = v.ProductCombo.Text,
                .WorkOrder = v.WorkOrderCombo.Text,
                .Barcode = barcode,
                .Hinge = hinge,
                .CartonNo = Integer.Parse(v.CartonText.Text.Trim()),
                .LineNo = Integer.Parse(v.LineNoText.Text.Trim()),
                .OperatorId = v.OperatorText.Text.Trim()
            }
        End Function

        Private Function ReadOptions() As InspectionOptions
            Dim v = _view
            Return New InspectionOptions With {
                .BarcodeTemplate = v.BarcodeFormatText.Text,
                .HingeTemplate = v.HingeFormatText.Text,
                .CheckChecksum34 = v.Checksum34Check.Checked,
                .CheckChecksum36 = v.Checksum36Check.Checked,
                .CheckDimension = v.DimensionCheck.Checked,
                .CheckElectrical = v.ElectricalCheck.Checked,
                .CheckAppearance = v.AppearanceCheck.Checked,
                .CheckHingeData = v.HingeDataCheck.Checked,
                .CheckBarcodeMatchesHinge = v.CompareCheck.Checked,
                .CheckAvi = v.AviCheck.Checked,
                .CheckRunningNo = v.RunningNoCheck.Checked,
                .RunningNoRange = v.RunningNoText.Text,
                .ElectricalInputFolder = v.EltInputFolder.Text,
                .ElectricalOutputFolder = v.EltOutputFolder.Text,
                .HingeFolder = v.HingeFolder.Text,
                .AviStation1Folder = v.AviStation1Folder.Text,
                .AviStation2Folder = v.AviStation2Folder.Text
            }
        End Function

        Private Function AskRunningNoDecision(rangeText As String, barcode As String) As RunningNoDecision
            Using dlg As New Dialog1()
                dlg.SetInfo(rangeText, barcode)
                dlg.ShowDialog(_view.Grid.FindForm())
                Select Case dlg.SelectedAction
                    Case "Rework" : Return RunningNoDecision.Rework
                    Case "Reject" : Return RunningNoDecision.Reject
                    Case Else : Return RunningNoDecision.None
                End Select
            End Using
        End Function

        Private Sub SetStatus(text As String, color As Color)
            _view.StatusLabel.Text = text
            _view.StatusLabel.BackColor = color
        End Sub

#End Region

    End Class

End Namespace
