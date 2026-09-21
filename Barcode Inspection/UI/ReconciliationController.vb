Option Strict On

Imports System.Windows.Forms
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain

Namespace UI

    ''' <summary>The controls of the Barcode Reconciliation tab.</summary>
    Public Class ReconciliationView
        Public Property ProductCombo As ComboBox
        Public Property BarcodeText As TextBox
        Public Property AddButton As Button
        Public Property ScannedGrid As DataGridView
        Public Property ScannedCount As TextBox
        Public Property ScannedClearSelectedMenuItem As ToolStripMenuItem
        Public Property ScannedClearAllMenuItem As ToolStripMenuItem

        Public Property WorkOrderCombo As ComboBox
        Public Property CartonCombo As ComboBox
        Public Property CompareButton As Button
        Public Property MissingGrid As DataGridView
        Public Property MissingCount As TextBox
        Public Property MissingClearSelectedMenuItem As ToolStripMenuItem
        Public Property MissingClearAllMenuItem As ToolStripMenuItem
        Public Property MissingDeleteSelectedMenuItem As ToolStripMenuItem
        Public Property MissingDeleteAllMenuItem As ToolStripMenuItem

        Public Property ResetButton As Button
    End Class

    ''' <summary>
    ''' Drives the Barcode Reconciliation tab: scan the parts that are physically in a carton, then list the
    ''' parts the database says passed for that work order / carton but were not scanned.
    ''' </summary>
    Public Class ReconciliationController

        ' Both grids show these columns, in the designer's order.
        Private Shared ReadOnly GridColumns As String() = {"part_no", "barcode", "work_order", "carton_box_no"}

        Private ReadOnly _view As ReconciliationView
        Private ReadOnly _repository As ReconciliationRepository
        Private ReadOnly _workOrders As InspectionRepository
        Private ReadOnly _scanned As New DataTable()
        Private ReadOnly _missing As New DataTable()

        Public Sub New(view As ReconciliationView, repository As ReconciliationRepository, workOrders As InspectionRepository)
            _view = view
            _repository = repository
            _workOrders = workOrders

            For Each table In {_scanned, _missing}
                table.Columns.Add("part_no", GetType(Integer))
                table.Columns.Add("barcode", GetType(String))
                table.Columns.Add("work_order", GetType(String))
                table.Columns.Add("carton_box_no", GetType(Integer))
            Next

            WireEvents()
        End Sub

        Private Sub WireEvents()
            With _view
                AddHandler .ProductCombo.SelectedIndexChanged, AddressOf ProductChanged
                AddHandler .WorkOrderCombo.SelectedIndexChanged, AddressOf WorkOrderChanged
                AddHandler .BarcodeText.KeyDown, Sub(s, e) UiKit.OnEnter(e, Sub() .AddButton.PerformClick())
                AddHandler .AddButton.Click, AddressOf AddScannedBarcode
                AddHandler .CompareButton.Click, AddressOf FindMissing
                AddHandler .ResetButton.Click, AddressOf ResetAll

                AddHandler .ScannedClearSelectedMenuItem.Click, Sub() RemoveSelectedRows(.ScannedGrid, _scanned, .ScannedCount)
                AddHandler .ScannedClearAllMenuItem.Click, Sub() ClearRows(_scanned, .ScannedCount)
                AddHandler .MissingClearSelectedMenuItem.Click, Sub() RemoveSelectedRows(.MissingGrid, _missing, .MissingCount)
                AddHandler .MissingClearAllMenuItem.Click, Sub() ClearRows(_missing, .MissingCount)
                AddHandler .MissingDeleteSelectedMenuItem.Click, Async Sub() Await DeleteFromDatabaseAsync(allRows:=False)
                AddHandler .MissingDeleteAllMenuItem.Click, Async Sub() Await DeleteFromDatabaseAsync(allRows:=True)
            End With
        End Sub

#Region "Filters"

        Private Async Sub ProductChanged(sender As Object, e As EventArgs)
            If _view.ProductCombo.SelectedIndex < 0 Then Return
            Dim productName = _view.ProductCombo.SelectedItem.ToString()
            ResetCombo(_view.WorkOrderCombo)

            Try
                Dim workOrders = Await _workOrders.GetWorkOrdersAsync(productName)
                _view.WorkOrderCombo.Items.AddRange(workOrders.Cast(Of Object)().ToArray())
            Catch ex As Exception
                UiKit.Error($"Error loading work orders: {ex.Message}")
            End Try
        End Sub

        Private Async Sub WorkOrderChanged(sender As Object, e As EventArgs)
            If _view.WorkOrderCombo.SelectedIndex < 0 Then Return
            Dim workOrder = _view.WorkOrderCombo.SelectedItem.ToString()
            ResetCombo(_view.CartonCombo)

            Try
                Dim cartons = Await _repository.GetCartonNumbersAsync(workOrder)
                _view.CartonCombo.Items.AddRange(cartons.Cast(Of Object)().ToArray())
            Catch ex As Exception
                UiKit.Error($"Error loading carton box no: {ex.Message}")
            End Try
        End Sub

        Private Shared Sub ResetCombo(combo As ComboBox)
            combo.Items.Clear()
            combo.SelectedIndex = -1
            combo.Text = ""
        End Sub

#End Region

#Region "Scanned parts"

        ''' <summary>Adds a scanned barcode to the "in the carton" list after checking it passed for the chosen product.</summary>
        Private Async Sub AddScannedBarcode(sender As Object, e As EventArgs)
            If _view.ProductCombo.SelectedIndex < 0 Then
                UiKit.Warning("Please select product name.")
                Return
            End If
            Dim barcode = _view.BarcodeText.Text.Trim()
            If barcode = "" Then
                UiKit.Invalid("Please input barcode.", focus:=_view.BarcodeText)
                Return
            End If

            Dim productName = _view.ProductCombo.SelectedItem.ToString()
            Try
                Dim record = Await _repository.FindPassedAsync(barcode)
                If record Is Nothing Then
                    UiKit.Invalid("Barcode not found in passed records.", "Not Found", _view.BarcodeText)
                ElseIf record.ProductName <> productName Then
                    UiKit.Invalid($"Barcode belongs to another product.{Environment.NewLine}Selected: {productName}{Environment.NewLine}Actual: {record.ProductName}",
                                  "Product Mismatch", _view.BarcodeText)
                ElseIf ContainsBarcode(_scanned, record.Barcode) Then
                    UiKit.Info("This barcode is already in the list.", "Duplicate")
                Else
                    AddRow(_scanned, record)
                    Show(_view.ScannedGrid, _scanned, _view.ScannedCount)
                    SelectLastRow(_view.ScannedGrid)
                End If
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
            Catch ex As Exception
                UiKit.Error($"Error: {ex.Message}")
            End Try

            _view.BarcodeText.SelectAll()
            _view.BarcodeText.Focus()
        End Sub

#End Region

#Region "Missing parts"

        ''' <summary>Lists passed records for the work order / carton that are not in the scanned list.</summary>
        Private Async Sub FindMissing(sender As Object, e As EventArgs)
            With _view
                For Each combo As ComboBox In { .WorkOrderCombo, .CartonCombo, .ProductCombo}
                    If String.IsNullOrWhiteSpace(combo.Text) Then combo.SelectedIndex = -1
                Next
            End With

            Dim filter As ReconciliationFilter = Nothing
            Dim problem As String = Nothing
            If Not ReconciliationFilter.TryCreate(_view.ProductCombo.Text, _view.WorkOrderCombo.Text, _view.CartonCombo.Text, filter, problem) Then
                UiKit.Warning(problem, If(problem.StartsWith("Invalid"), "Invalid Input", "Input Required"))
                Return
            End If

            Try
                Dim passed = Await _repository.GetPassedAsync(filter)
                Dim scannedBarcodes = _scanned.Rows.Cast(Of DataRow)().Select(Function(r) r("barcode").ToString())

                _missing.Clear()
                For Each record In Reconciliation.FindMissing(passed, scannedBarcodes)
                    AddRow(_missing, record)
                Next
                Show(_view.MissingGrid, _missing, _view.MissingCount)
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
            Catch ex As Exception
                UiKit.Error($"Error: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' Deletes passed records from the database. Admin only: these are final-inspection results.
        ''' </summary>
        Private Async Function DeleteFromDatabaseAsync(allRows As Boolean) As Task
            If _missing.Rows.Count = 0 Then
                If allRows Then UiKit.Info("No data to delete.")
                Return
            End If

            Dim rows = If(allRows,
                          _missing.Rows.Cast(Of DataRow)().ToList(),
                          SelectedDataRows(_view.MissingGrid))
            If rows.Count = 0 Then
                UiKit.Warning("Please select at least one row to delete.", "No Selection")
                Return
            End If

            Dim question = If(allRows, "Are you sure you want to delete ALL records shown in this list from database?",
                                       "Are you sure you want to delete the selected record(s) from database?")
            If Not UiKit.ConfirmAdminDelete(question) Then Return

            Try
                Await _repository.DeletePassedAsync(rows.Select(Function(r) r("barcode").ToString()))
            Catch ex As DatabaseUnavailableException
                UiKit.Error("Cannot connect to database.", "Connection Error")
                Return
            Catch ex As Exception
                UiKit.Error($"Delete failed: {ex.Message}")
                Return
            End Try

            For Each row In rows
                _missing.Rows.Remove(row)
            Next
            _missing.AcceptChanges()
            _view.MissingCount.Text = _missing.Rows.Count.ToString()
        End Function

#End Region

#Region "Grid helpers"

        Private Shared Sub AddRow(table As DataTable, record As PassedRecord)
            table.Rows.Add(record.PartNo, record.Barcode, record.WorkOrder, record.CartonNo)
        End Sub

        Private Shared Function ContainsBarcode(table As DataTable, barcode As String) As Boolean
            Return table.Rows.Cast(Of DataRow)().Any(Function(r) String.Equals(r("barcode").ToString(), barcode, StringComparison.OrdinalIgnoreCase))
        End Function

        Private Shared Sub Show(grid As DataGridView, table As DataTable, count As TextBox)
            If grid.DataSource IsNot table Then
                grid.AutoGenerateColumns = False
                For i As Integer = 0 To GridColumns.Length - 1
                    grid.Columns(i).DataPropertyName = GridColumns(i)
                Next
                grid.DataSource = table
            End If
            count.Text = table.Rows.Count.ToString()
        End Sub

        Private Shared Sub SelectLastRow(grid As DataGridView)
            If grid.Rows.Count = 0 Then Return
            Dim lastIndex = grid.Rows.Count - 1
            grid.ClearSelection()
            grid.CurrentCell = grid.Rows(lastIndex).Cells(0)
            grid.Rows(lastIndex).Selected = True
            grid.FirstDisplayedScrollingRowIndex = lastIndex
        End Sub

        ''' <summary>The data rows behind the selected grid rows (correct even when the grid is sorted).</summary>
        Private Shared Function SelectedDataRows(grid As DataGridView) As List(Of DataRow)
            Return grid.SelectedRows.Cast(Of DataGridViewRow)().
                Select(Function(r) TryCast(r.DataBoundItem, DataRowView)).
                Where(Function(v) v IsNot Nothing).
                Select(Function(v) v.Row).
                ToList()
        End Function

        ''' <summary>Removes the selected rows from the screen only; the database is not touched.</summary>
        Private Shared Sub RemoveSelectedRows(grid As DataGridView, table As DataTable, count As TextBox)
            If table.Rows.Count = 0 Then Return
            Dim rows = SelectedDataRows(grid)
            If rows.Count = 0 Then
                UiKit.Warning("Please select at least one row to clear.", "No Selection")
                Return
            End If
            For Each row In rows
                table.Rows.Remove(row)
            Next
            table.AcceptChanges()
            count.Text = table.Rows.Count.ToString()
        End Sub

        Private Shared Sub ClearRows(table As DataTable, count As TextBox)
            If table.Rows.Count = 0 Then Return
            table.Clear()
            count.Text = "0"
        End Sub

        Private Sub ResetAll(sender As Object, e As EventArgs)
            With _view
                .ProductCombo.SelectedIndex = -1
                .ProductCombo.Text = ""
                .BarcodeText.Clear()
                ResetCombo(.WorkOrderCombo)
                ResetCombo(.CartonCombo)

                _scanned.Clear()
                _missing.Clear()
                .ScannedCount.Clear()
                .MissingCount.Clear()
            End With
        End Sub

#End Region

    End Class

End Namespace
