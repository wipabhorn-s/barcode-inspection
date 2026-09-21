Option Strict On

Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services

Namespace UI

    ''' <summary>The controls of the Combined tab.</summary>
    Public Class CombinedCartonView
        Public Property ProductCombo As ComboBox
        Public Property ItemCodeLabel As Label
        Public Property BarcodeFormatText As TextBox
        Public Property CapacityText As TextBox
        Public Property OperatorText As TextBox
        Public Property CartonText As TextBox
        Public Property LoadButton As Button
        Public Property BarcodeText As TextBox
        Public Property AddButton As Button
        Public Property StatusLabel As Label
        Public Property CountText As TextBox
        Public Property Grid As DataGridView
    End Class

    ''' <summary>Drives the Combined tab: open a combined carton, then scan passed parts into it.</summary>
    Public Class CombinedCartonController

        Private Shared ReadOnly GridColumns As String() = {"id", "barcode", "work_order", "combined_carton_no", "combined_date", "combined_time"}

        Private ReadOnly _view As CombinedCartonView
        Private ReadOnly _repository As CombinedCartonRepository
        Private ReadOnly _products As ProductSpecRepository
        Private ReadOnly _service As CombinedCartonService
        Private _capacity As Integer
        Private _isAdding As Boolean

        Public Sub New(view As CombinedCartonView, repository As CombinedCartonRepository, products As ProductSpecRepository)
            _view = view
            _repository = repository
            _products = products
            _service = New CombinedCartonService(repository)

            WireEvents()
        End Sub

        Private Sub WireEvents()
            With _view
                AddHandler .ProductCombo.SelectedIndexChanged, AddressOf ProductChanged
                AddHandler .ProductCombo.TextChanged, Sub() If .ProductCombo.Text = "" Then ResetProductDetails()
                UiKit.KeepLastCharacters(.OperatorText, 7)
                AddHandler .OperatorText.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub() .CartonText.Focus())
                AddHandler .CartonText.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub() .LoadButton.PerformClick())
                AddHandler .LoadButton.Click, AddressOf LoadCarton
                AddHandler .BarcodeText.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub() .AddButton.PerformClick())
                AddHandler .BarcodeText.TextChanged, Sub() SetStatus("Idle", Color.LemonChiffon)
                AddHandler .AddButton.Click, AddressOf AddBarcode
            End With
        End Sub

        Private Async Sub ProductChanged(sender As Object, e As EventArgs)
            If _view.ProductCombo.SelectedIndex < 0 Then Return
            Dim productName = _view.ProductCombo.SelectedItem.ToString()
            ResetProductDetails()

            Try
                Dim spec = Await _products.GetAsync(productName)
                If spec Is Nothing Then Return
                _capacity = spec.Quantity
                _view.ItemCodeLabel.Text = spec.ItemCode
                _view.BarcodeFormatText.Text = spec.BarcodeSpec
                _view.CapacityText.Text = spec.Quantity.ToString()
            Catch ex As Exception
                UiKit.Error($"Error loading product data: {ex.Message}")
            End Try
        End Sub

        Private Sub ResetProductDetails()
            _capacity = 0
            With _view
                .ItemCodeLabel.Text = String.Empty
                For Each box In { .BarcodeFormatText, .CapacityText, .CountText, .CartonText, .BarcodeText}
                    box.Clear()
                Next
                .BarcodeText.Enabled = False
                .Grid.DataSource = Nothing
                .Grid.Rows.Clear()
            End With
        End Sub

        Private Function ValidateSetup() As Boolean
            With _view
                If .ProductCombo.SelectedIndex < 0 Then Return UiKit.Invalid("Please select product name.")
                If String.IsNullOrWhiteSpace(.OperatorText.Text) Then Return UiKit.Invalid("Please enter operator id.", focus:= .OperatorText)

                Dim cartonNo As Integer
                If Not Integer.TryParse(.CartonText.Text.Trim(), cartonNo) OrElse cartonNo <= 0 Then
                    Return UiKit.Invalid("Please enter a valid carton box no. (number greater than 0).", "Invalid Input", .CartonText)
                End If
            End With
            Return True
        End Function

        Private ReadOnly Property ProductName As String
            Get
                Return _view.ProductCombo.SelectedItem.ToString()
            End Get
        End Property

        Private ReadOnly Property CartonNo As Integer
            Get
                Return Integer.Parse(_view.CartonText.Text.Trim())
            End Get
        End Property

#Region "Carton grid"

        Private Async Sub LoadCarton(sender As Object, e As EventArgs)
            If Not ValidateSetup() Then Return
            If Await LoadGridAsync() Then CheckCartonFull(showWarning:=True)
        End Sub

        Private Async Function LoadGridAsync() As Task(Of Boolean)
            Try
                Dim rows = Await _repository.GetCartonAsync(ProductName, CartonNo)
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
            grid.CurrentCell = grid.Rows(lastIndex).Cells(1)
        End Sub

        ''' <summary>Disables scanning when the carton is full; otherwise puts the cursor in the barcode box.</summary>
        Private Sub CheckCartonFull(showWarning As Boolean)
            Dim count As Integer
            If _capacity <= 0 OrElse Not Integer.TryParse(_view.CountText.Text, count) Then Return

            If count >= _capacity Then
                _view.BarcodeText.Enabled = False
                If showWarning Then
                    UiKit.Warning("Combined carton is full. Cannot add more records.", "Carton Full")
                    _view.CartonText.Focus()
                    _view.CartonText.SelectAll()
                End If
                Return
            End If

            _view.BarcodeText.Enabled = True
            _view.BarcodeText.Focus()
            _view.BarcodeText.SelectAll()
        End Sub

        ''' <summary>Appends the part just added, or reloads the carton if that is not possible.</summary>
        Private Async Function AddSavedRowAsync(barcode As String) As Task
            Dim gridTable = TryCast(_view.Grid.DataSource, DataTable)
            Try
                Dim newRow = Await _repository.GetLatestAsync(barcode)
                If gridTable IsNot Nothing AndAlso newRow.Rows.Count > 0 Then
                    gridTable.ImportRow(newRow.Rows(0))
                    gridTable.AcceptChanges()
                    _view.CountText.Text = gridTable.Rows.Count.ToString()
                    SelectLastRow()
                    Return
                End If
            Catch ex As Exception
                Debug.WriteLine($"Combined AddSavedRow Error: {ex.Message}")
            End Try

            Await LoadGridAsync()
        End Function

#End Region

#Region "Scanning"

        Private Async Sub AddBarcode(sender As Object, e As EventArgs)
            If _isAdding Then Return ' ignore a second scan while the first is still being saved
            If Not ValidateSetup() Then Return

            Dim barcode = _view.BarcodeText.Text.Trim()
            If barcode = "" Then
                UiKit.Invalid("Please enter barcode.", focus:=_view.BarcodeText)
                Return
            End If

            _isAdding = True
            _view.AddButton.Enabled = False
            Message.ResetErrorFlag()
            Try
                Dim result = Await _service.AddAsync(New CombineRequest With {
                    .ProductName = ProductName,
                    .CartonNo = CartonNo,
                    .Capacity = _capacity,
                    .Barcode = barcode,
                    .OperatorId = _view.OperatorText.Text.Trim()
                })

                If result.Success Then
                    SetStatus("PASS", Color.PaleGreen)
                    Await AddSavedRowAsync(barcode)
                    CheckCartonFull(showWarning:=True)
                Else
                    SetStatus("FAIL", Color.LightCoral)
                    Message.ResultFailCase(barcode, result.FailureMessage, CInt(FailurePriority.Critical))
                    Message.ShowFirstError()
                End If
            Catch ex As Exception
                SetStatus("FAIL", Color.LightCoral)
                UiKit.Error($"Database Error: {ex.Message}")
            Finally
                _isAdding = False
                _view.AddButton.Enabled = True
                _view.BarcodeText.SelectAll()
                _view.BarcodeText.Focus()
            End Try
        End Sub

        Private Sub SetStatus(text As String, color As Color)
            _view.StatusLabel.Text = text
            _view.StatusLabel.BackColor = color
        End Sub

#End Region

    End Class

End Namespace
