Option Strict On

Imports System.Windows.Forms
Imports Barcode_Inspection.Data

Namespace UI

    ''' <summary>The controls of the Final Data tab.</summary>
    Public Class FinalReportView
        Public Property ProductCombo As ComboBox
        Public Property WorkOrderFilter As CheckBox
        Public Property WorkOrderCombo As ComboBox
        Public Property CartonFilter As CheckBox
        Public Property CartonText As TextBox
        Public Property DateFilter As CheckBox
        Public Property DateFrom As DateTimePicker
        Public Property DateTo As DateTimePicker
        Public Property BarcodeFilter As CheckBox
        Public Property BarcodeText As TextBox
        Public Property HingeFilter As CheckBox
        Public Property HingeText As TextBox
        ''' <summary>"Defect": also show the product's failed scans.</summary>
        Public Property DefectFilter As CheckBox
        Public Property CombinedFilter As CheckBox
        Public Property CombinedCombo As ComboBox

        Public Property SearchButton As Button
        Public Property ClearButton As Button
        Public Property ExportButton As Button

        Public Property PassGrid As DataGridView
        Public Property PassCount As TextBox
        Public Property PassClearSelectedMenuItem As ToolStripMenuItem
        Public Property PassClearAllMenuItem As ToolStripMenuItem
        Public Property PassDeleteMenuItem As ToolStripMenuItem
        Public Property DeleteCombinedMenuItem As ToolStripMenuItem

        Public Property FailGrid As DataGridView
        Public Property FailCount As TextBox
        Public Property FailClearAllMenuItem As ToolStripMenuItem
        Public Property FailDeleteMenuItem As ToolStripMenuItem
    End Class

    ''' <summary>Drives the Final Data tab: search final-inspection results, export, and delete (admin only).</summary>
    Public Class FinalReportController

        ' Grid column order as laid out in the designer.
        Private Shared ReadOnly FailColumns As String() = {
            "id", "part_no", "barcode", "barcode_hinge", "product_name", "work_order", "carton_box_no", "line_no",
            "operator_id", "inspection_date", "inspection_time", "dimension_result", "electrical_result",
            "appearance_result", "hinge_max_15", "hinge_avg_15", "hinge_max_120", "hinge_avg_120", "hinge_angle",
            "hinge_vendor", "hinge_date", "mc_1_result", "mc_2_result", "remark"}

        Private Shared ReadOnly PassColumns As String() = FailColumns.Concat(
            {"combined_carton_no", "combined_operator_id", "combined_date", "combined_time"}).ToArray()

        Private Const BarcodeColumn As Integer = 2
        Private Const CombinedCartonColumn As Integer = 24

        Private ReadOnly _view As FinalReportView
        Private ReadOnly _repository As FinalReportRepository
        Private ReadOnly _pass As ResultGrid
        Private ReadOnly _fail As ResultGrid

        Public Sub New(view As FinalReportView, repository As FinalReportRepository)
            _view = view
            _repository = repository
            _pass = New ResultGrid(view.PassGrid, view.PassCount, PassColumns)
            _fail = New ResultGrid(view.FailGrid, view.FailCount, FailColumns)

            WireEvents()
        End Sub

        Private Sub WireEvents()
            With _view
                UiKit.BindFilter(.WorkOrderFilter, .WorkOrderCombo)
                UiKit.BindFilter(.CartonFilter, .CartonText)
                UiKit.BindFilter(.DateFilter, .DateFrom, .DateTo)
                UiKit.BindFilter(.BarcodeFilter, .BarcodeText)
                UiKit.BindFilter(.HingeFilter, .HingeText)
                UiKit.BindFilter(.CombinedFilter, .CombinedCombo)
                AddHandler .CombinedFilter.CheckedChanged, AddressOf CombinedFilterChanged

                AddHandler .ProductCombo.SelectedIndexChanged, AddressOf ProductChanged
                For Each box As TextBox In { .CartonText, .BarcodeText, .HingeText}
                    Dim current = box
                    AddHandler current.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub()
                                                                            _view.SearchButton.PerformClick()
                                                                            current.SelectAll()
                                                                            current.Focus()
                                                                        End Sub)
                Next

                AddHandler .SearchButton.Click, AddressOf Search
                AddHandler .ClearButton.Click, AddressOf ClearAll
                AddHandler .ExportButton.Click, AddressOf Export

                AddHandler .PassClearSelectedMenuItem.Click, AddressOf ClearSelectedPassRows
                AddHandler .PassClearAllMenuItem.Click, Sub() _pass.ClearRows()
                AddHandler .PassDeleteMenuItem.Click, Async Sub() Await DeleteSelectedAsync(_pass, AddressOf _repository.DeletePassByIdsAsync, "Are you sure you want to delete the selected record(s)?")
                AddHandler .DeleteCombinedMenuItem.Click, AddressOf DeleteCombined
                AddHandler .FailClearAllMenuItem.Click, Sub() _fail.ClearRows()
                AddHandler .FailDeleteMenuItem.Click, Async Sub() Await DeleteSelectedAsync(_fail, AddressOf _repository.DeleteFailByIdsAsync, "Are you sure you want to delete the selected record(s) from database?")
            End With
        End Sub

#Region "Filters"

        Private Async Sub ProductChanged(sender As Object, e As EventArgs)
            If _view.ProductCombo.SelectedIndex < 0 Then Return

            Dim productName = _view.ProductCombo.SelectedItem.ToString()
            _view.WorkOrderCombo.Items.Clear()
            _view.WorkOrderCombo.SelectedIndex = -1
            _view.WorkOrderCombo.Text = ""

            Try
                Dim workOrders = Await _repository.GetWorkOrdersAsync(productName)
                _view.WorkOrderCombo.Items.AddRange(workOrders.Cast(Of Object)().ToArray())
            Catch ex As Exception
                UiKit.Error($"Error loading work orders: {ex.Message}")
            End Try
        End Sub

        ''' <summary>Ticking "Carton Combined" lists the product's combined cartons.</summary>
        Private Async Sub CombinedFilterChanged(sender As Object, e As EventArgs)
            If Not _view.CombinedFilter.Checked Then Return

            If _view.ProductCombo.SelectedIndex < 0 Then
                UiKit.Warning("Please select Product first.")
                _view.CombinedFilter.Checked = False
                Return
            End If

            _view.CombinedCombo.Items.Clear()
            Try
                Dim cartons = Await _repository.GetCombinedCartonsAsync(_view.ProductCombo.SelectedItem.ToString())
                _view.CombinedCombo.Items.AddRange(cartons.Cast(Of Object)().ToArray())
            Catch ex As Exception
                UiKit.Error($"Error loading combined carton: {ex.Message}")
            End Try
        End Sub

        ''' <summary>Only barcode and/or hinge is ticked: results are added to the grid instead of replacing it.</summary>
        Private Function IsBarcodeOnlySearch() As Boolean
            With _view
                Return (.BarcodeFilter.Checked OrElse .HingeFilter.Checked) AndAlso
                       Not (.WorkOrderFilter.Checked OrElse .CartonFilter.Checked OrElse .DateFilter.Checked OrElse .CombinedFilter.Checked)
            End With
        End Function

        Private Function ValidateFilters() As Boolean
            With _view
                ' "Defect" loads by product, so it always needs a product.
                Dim needsProduct = Not IsBarcodeOnlySearch() OrElse .DefectFilter.Checked
                If needsProduct AndAlso .ProductCombo.SelectedIndex < 0 Then Return UiKit.Invalid("Please select Product.")

                If Not (.WorkOrderFilter.Checked OrElse .CartonFilter.Checked OrElse .DateFilter.Checked OrElse .BarcodeFilter.Checked OrElse
                        .HingeFilter.Checked OrElse .DefectFilter.Checked OrElse .CombinedFilter.Checked) Then
                    Return UiKit.Invalid("Please select at least one condition.")
                End If

                If .WorkOrderFilter.Checked AndAlso .WorkOrderCombo.SelectedIndex < 0 Then Return UiKit.Invalid("Please select work order.")

                Dim cartonNo As Integer
                If .CartonFilter.Checked AndAlso Not Integer.TryParse(.CartonText.Text.Trim(), cartonNo) Then
                    Return UiKit.Invalid("Please input integer value for carton box no.", "Invalid Input", .CartonText)
                End If

                If .BarcodeFilter.Checked AndAlso String.IsNullOrWhiteSpace(.BarcodeText.Text) Then Return UiKit.Invalid("Please input barcode.", focus:= .BarcodeText)
                If .HingeFilter.Checked AndAlso String.IsNullOrWhiteSpace(.HingeText.Text) Then Return UiKit.Invalid("Please input hinge or CMOS.", focus:= .HingeText)
                If .CombinedFilter.Checked AndAlso .CombinedCombo.SelectedIndex < 0 Then Return UiKit.Invalid("Please select combined carton box no.")
            End With
            Return True
        End Function

        Private Function BuildFilter() As FinalReportFilter
            Dim v = _view
            Dim filter As New FinalReportFilter()

            If v.ProductCombo.SelectedIndex >= 0 Then filter.ProductName = v.ProductCombo.SelectedItem.ToString()
            If v.WorkOrderFilter.Checked Then filter.WorkOrder = v.WorkOrderCombo.SelectedItem.ToString()
            If v.CartonFilter.Checked Then filter.CartonNo = Integer.Parse(v.CartonText.Text.Trim())
            If v.DateFilter.Checked Then
                filter.DateFrom = v.DateFrom.Value
                filter.DateTo = v.DateTo.Value
            End If
            If v.BarcodeFilter.Checked Then filter.Barcode = v.BarcodeText.Text.Trim()
            If v.HingeFilter.Checked Then filter.Hinge = v.HingeText.Text.Trim()

            Dim combined = TryCast(v.CombinedCombo.SelectedItem, CombinedCartonOption)
            If v.CombinedFilter.Checked AndAlso combined IsNot Nothing Then filter.CombinedCartonNo = combined.CartonNo

            Return filter
        End Function

#End Region

#Region "Search"

        Private Async Sub Search(sender As Object, e As EventArgs)
            If Not ValidateFilters() Then Return

            If Not IsBarcodeOnlySearch() Then
                _pass.Clear()
                _fail.Clear()
            End If

            With _view
                Dim hasPassFilter = .WorkOrderFilter.Checked OrElse .CartonFilter.Checked OrElse .DateFilter.Checked OrElse
                                    .BarcodeFilter.Checked OrElse .HingeFilter.Checked

                ' "Defect" on its own shows only the failed scans.
                If .DefectFilter.Checked AndAlso Not hasPassFilter Then
                    Await LoadFailuresAsync()
                    If _fail.Table IsNot Nothing AndAlso _fail.Table.Rows.Count = 0 Then UiKit.Info("No FAIL data found.")
                    Return
                End If

                Await LoadPassAsync()
                If .DefectFilter.Checked Then Await LoadFailuresAsync()
            End With
        End Sub

        Private Async Function LoadPassAsync() As Task
            Try
                Dim rows = Await _repository.SearchPassAsync(BuildFilter())
                If rows.Rows.Count = 0 Then
                    UiKit.Info("No data found for this condition.")
                    Return
                End If

                _pass.Merge(rows)
                _pass.SelectLastRow()
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
            Catch ex As Exception
                UiKit.Error($"Error loading data: {ex.Message}")
            End Try
        End Function

        Private Async Function LoadFailuresAsync() As Task
            Try
                _fail.Show(Await _repository.GetFailuresAsync(_view.ProductCombo.SelectedItem.ToString()))
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
            Catch ex As Exception
                UiKit.Error($"Error loading FAIL data: {ex.Message}")
            End Try
        End Function

        Private Sub ClearAll(sender As Object, e As EventArgs)
            _pass.Clear()
            _fail.Clear()

            With _view
                For Each check As CheckBox In { .WorkOrderFilter, .CartonFilter, .DateFilter, .BarcodeFilter, .HingeFilter, .DefectFilter, .CombinedFilter}
                    check.Checked = False
                Next
                For Each combo As ComboBox In { .ProductCombo, .WorkOrderCombo, .CombinedCombo}
                    combo.SelectedIndex = -1
                    combo.Text = ""
                Next
            End With
        End Sub

        Private Sub Export(sender As Object, e As EventArgs)
            If _pass.IsEmpty Then
                UiKit.Info("No data to export.")
                Return
            End If
            UiKit.ExportToCsv("Barcode", "Export barcode data to CSV", _pass.Grid, _pass.Grid)
        End Sub

#End Region

#Region "Right-click menus"

        Private Sub ClearSelectedPassRows(sender As Object, e As EventArgs)
            If _pass.IsEmpty Then Return
            If _pass.Grid.SelectedRows.Count = 0 Then
                UiKit.Warning("Please select at least one row to clear.", "No Selection")
                Return
            End If
            _pass.RemoveIds(_pass.SelectedIds())
        End Sub

        Private Async Function DeleteSelectedAsync(target As ResultGrid, delete As Func(Of IEnumerable(Of Long), Task), question As String) As Task
            If target.IsEmpty Then Return
            If target.Grid.SelectedRows.Count = 0 Then
                UiKit.Warning("Please select at least one row to delete.", "No Selection")
                Return
            End If
            If Not UiKit.ConfirmAdminDelete(question) Then Return

            Dim ids = target.SelectedIds()
            If ids.Count = 0 Then
                UiKit.Error("No valid ID found.")
                Return
            End If

            Try
                Await delete(ids)
                target.RemoveIds(ids)
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
            Catch ex As Exception
                UiKit.Error($"Delete failed: {ex.Message}")
            End Try
        End Function

        ''' <summary>Takes the selected parts out of their combined carton. The parts stay in the pass list.</summary>
        Private Async Sub DeleteCombined(sender As Object, e As EventArgs)
            If _pass.IsEmpty Then Return
            If _pass.Grid.SelectedRows.Count = 0 Then
                UiKit.Warning("Please select at least one row to delete.", "No Selection")
                Return
            End If

            Dim selected = _pass.Grid.SelectedRows.Cast(Of DataGridViewRow)().
                Where(Function(r) Not IsBlank(r.Cells(CombinedCartonColumn).Value) AndAlso Not IsBlank(r.Cells(BarcodeColumn).Value)).
                ToList()

            If selected.Count = 0 Then
                UiKit.Warning("Selected row(s) have no combined data.", "No Combined Data")
                Return
            End If

            If Not UiKit.ConfirmAdminDelete($"Are you sure you want to delete {selected.Count} combined record(s)?") Then Return

            Try
                Await _repository.DeleteCombinedAsync(selected.Select(Function(r) r.Cells(BarcodeColumn).Value.ToString()))
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
                Return
            Catch ex As Exception
                UiKit.Error($"Delete failed: {ex.Message}")
                Return
            End Try

            ' The pass record still exists, so keep the row and only blank its combined-carton columns.
            For Each row In selected
                Dim data = DirectCast(row.DataBoundItem, DataRowView).Row
                For Each column In {"combined_carton_no", "combined_operator_id", "combined_date", "combined_time"}
                    data(column) = DBNull.Value
                Next
            Next
            _pass.Table.AcceptChanges()
        End Sub

        Private Shared Function IsBlank(value As Object) As Boolean
            Return value Is Nothing OrElse TypeOf value Is DBNull OrElse String.IsNullOrEmpty(value.ToString())
        End Function

#End Region

    End Class

End Namespace
