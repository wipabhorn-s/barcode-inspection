Option Strict On

Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain

Namespace UI

    ''' <summary>The controls of one station's report tab.</summary>
    Public Class StationReportView
        Public Property ProductCombo As ComboBox
        Public Property WorkOrderFilter As CheckBox
        Public Property WorkOrderCombo As ComboBox
        Public Property DateFilter As CheckBox
        Public Property DateFrom As DateTimePicker
        Public Property DateTo As DateTimePicker
        Public Property BarcodeFilter As CheckBox
        Public Property BarcodeText As TextBox
        Public Property HingeFilter As CheckBox
        Public Property HingeText As TextBox
        Public Property SearchButton As Button
        Public Property ClearButton As Button
        Public Property ExportButton As Button
        Public Property PassGrid As DataGridView
        Public Property PassCount As TextBox
        Public Property PassClearMenuItem As ToolStripMenuItem
        Public Property PassDeleteMenuItem As ToolStripMenuItem
        Public Property FailGrid As DataGridView
        Public Property FailCount As TextBox
        Public Property FailClearMenuItem As ToolStripMenuItem
        Public Property FailDeleteMenuItem As ToolStripMenuItem
    End Class

    ''' <summary>
    ''' Drives one station's report tab: search passed and failed records, export them, and delete
    ''' records (admin password required). The pass and fail grids share every piece of code.
    ''' </summary>
    Public Class StationReportController

        Private ReadOnly _info As StationInfo
        Private ReadOnly _view As StationReportView
        Private ReadOnly _repository As StationRepository
        Private ReadOnly _pass As ResultGrid
        Private ReadOnly _fail As ResultGrid

        Public Sub New(info As StationInfo, view As StationReportView, repository As StationRepository)
            _info = info
            _view = view
            _repository = repository

            Dim columns = {"id", "part_no", "barcode", "barcode_hinge", "product_name", "work_order", "line_no",
                           "operator_id", "inspection_date", "inspection_time", info.ResultColumn, "remark"}
            _pass = New ResultGrid(view.PassGrid, view.PassCount, columns)
            _fail = New ResultGrid(view.FailGrid, view.FailCount, columns)

            WireEvents()
        End Sub

        Private Sub WireEvents()
            With _view
                UiKit.BindFilter(.WorkOrderFilter, .WorkOrderCombo)
                UiKit.BindFilter(.DateFilter, .DateFrom, .DateTo)
                UiKit.BindFilter(.BarcodeFilter, .BarcodeText)
                UiKit.BindFilter(.HingeFilter, .HingeText)

                AddHandler .ProductCombo.SelectedIndexChanged, AddressOf ProductChanged
                AddHandler .BarcodeText.KeyDown, Sub(s, e) SearchOnEnter(e, .BarcodeText)
                AddHandler .HingeText.KeyDown, Sub(s, e) SearchOnEnter(e, .HingeText)
                AddHandler .SearchButton.Click, AddressOf Search
                AddHandler .ClearButton.Click, AddressOf ClearAll
                AddHandler .ExportButton.Click, AddressOf Export
                AddHandler .PassClearMenuItem.Click, Sub() _pass.ClearRows()
                AddHandler .FailClearMenuItem.Click, Sub() _fail.ClearRows()
                AddHandler .PassDeleteMenuItem.Click, Async Sub() Await DeleteSelectedAsync(_pass)
                AddHandler .FailDeleteMenuItem.Click, Async Sub() Await DeleteSelectedAsync(_fail)
            End With
        End Sub

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

        Private Sub SearchOnEnter(e As KeyEventArgs, box As TextBox)
            UiKit.OnEnter(e, Sub()
                              _view.SearchButton.PerformClick()
                              box.SelectAll()
                              box.Focus()
                          End Sub)
        End Sub

#Region "Search"

        ''' <summary>Only barcode and/or hinge is ticked: results are added to the grid instead of replacing it.</summary>
        Private Function IsBarcodeOnlySearch() As Boolean
            With _view
                Return (.BarcodeFilter.Checked OrElse .HingeFilter.Checked) AndAlso Not .WorkOrderFilter.Checked AndAlso Not .DateFilter.Checked
            End With
        End Function

        Private Function ValidateFilters() As Boolean
            With _view
                If Not IsBarcodeOnlySearch() AndAlso .ProductCombo.SelectedIndex < 0 Then Return UiKit.Invalid("Please select Product.")

                If Not (.WorkOrderFilter.Checked OrElse .DateFilter.Checked OrElse .BarcodeFilter.Checked OrElse .HingeFilter.Checked) Then
                    Return UiKit.Invalid("Please select at least one condition.")
                End If

                If .WorkOrderFilter.Checked AndAlso .WorkOrderCombo.SelectedIndex < 0 Then Return UiKit.Invalid("Please select work order.")
                If .BarcodeFilter.Checked AndAlso String.IsNullOrWhiteSpace(.BarcodeText.Text) Then Return UiKit.Invalid("Please input barcode.", focus:= .BarcodeText)
                If .HingeFilter.Checked AndAlso String.IsNullOrWhiteSpace(.HingeText.Text) Then Return UiKit.Invalid("Please input hinge or CMOS.", focus:= .HingeText)
            End With
            Return True
        End Function

        Private Function BuildFilter() As StationSearchFilter
            Dim v = _view
            Dim filter As New StationSearchFilter()

            If v.ProductCombo.SelectedIndex >= 0 Then filter.ProductName = v.ProductCombo.SelectedItem.ToString()
            If v.WorkOrderFilter.Checked Then filter.WorkOrder = v.WorkOrderCombo.SelectedItem.ToString()
            If v.DateFilter.Checked Then
                filter.DateFrom = v.DateFrom.Value
                filter.DateTo = v.DateTo.Value
            End If
            If v.BarcodeFilter.Checked Then filter.Barcode = v.BarcodeText.Text.Trim()
            If v.HingeFilter.Checked Then filter.Hinge = v.HingeText.Text.Trim()

            Return filter
        End Function

        Private Async Sub Search(sender As Object, e As EventArgs)
            If Not ValidateFilters() Then Return

            If Not IsBarcodeOnlySearch() Then
                _pass.Clear()
                _fail.Clear()
            End If

            Dim filter = BuildFilter()
            Await LoadAsync(_pass, filter, passed:=True)
            Await LoadAsync(_fail, filter, passed:=False)

            If _pass.IsEmpty AndAlso _fail.IsEmpty Then UiKit.Info("No data found for this condition.")
        End Sub

        Private Async Function LoadAsync(target As ResultGrid, filter As StationSearchFilter, passed As Boolean) As Task
            Try
                target.Merge(Await _repository.SearchAsync(filter, passed))
                target.SelectLastRow()
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
            Catch ex As Exception
                UiKit.Error($"Error loading {_info.DisplayName.ToLower()} {If(passed, "pass", "fail")} data: {ex.Message}")
            End Try
        End Function

        Private Sub ClearAll(sender As Object, e As EventArgs)
            _pass.Clear()
            _fail.Clear()

            With _view
                .WorkOrderFilter.Checked = False
                .DateFilter.Checked = False
                .BarcodeFilter.Checked = False
                .HingeFilter.Checked = False

                .ProductCombo.SelectedIndex = -1
                .ProductCombo.Text = ""
                .WorkOrderCombo.SelectedIndex = -1
                .WorkOrderCombo.Text = ""
            End With
        End Sub

#End Region

        Private Async Function DeleteSelectedAsync(target As ResultGrid) As Task
            If target.IsEmpty Then Return
            If target.Grid.SelectedRows.Count = 0 Then
                UiKit.Warning("Please select at least one row to delete.", "No Selection")
                Return
            End If
            If Not UiKit.ConfirmAdminDelete("Are you sure you want to delete the selected record(s) from database?") Then Return

            Dim ids = target.SelectedIds()
            If ids.Count = 0 Then
                UiKit.Error("No valid ID found.")
                Return
            End If

            Try
                Await _repository.DeleteByIdsAsync(ids)
                target.RemoveIds(ids)
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
            Catch ex As Exception
                UiKit.Error($"Delete failed: {ex.Message}")
            End Try
        End Function

        ''' <summary>Failed rows first, then passed rows.</summary>
        Private Sub Export(sender As Object, e As EventArgs)
            If _pass.IsEmpty AndAlso _fail.IsEmpty Then
                UiKit.Info("No data to export.")
                Return
            End If
            UiKit.ExportToCsv(_info.DisplayName, $"Export {_info.DisplayName.ToLower()} data to CSV", _pass.Grid, _fail.Grid, _pass.Grid)
        End Sub

    End Class

End Namespace
