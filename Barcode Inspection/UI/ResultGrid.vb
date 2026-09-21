Option Strict On

Imports System.Windows.Forms

Namespace UI

    ''' <summary>
    ''' A grid of search results and its row count. Rows are kept in <see cref="Table"/> so they can be
    ''' added to (barcode-by-barcode searches) or removed from the screen without querying again.
    ''' </summary>
    Public Class ResultGrid

        Public ReadOnly Property Grid As DataGridView
        Public ReadOnly Property Count As TextBox
        Private ReadOnly _columns As String()

        ''' <summary>Nothing until the first search.</summary>
        Public Property Table As DataTable

        ''' <param name="columns">Data column shown in each grid column, in the designer's column order.</param>
        Public Sub New(grid As DataGridView, count As TextBox, columns As IEnumerable(Of String))
            Me.Grid = grid
            Me.Count = count
            _columns = columns.ToArray()
        End Sub

        Public ReadOnly Property IsEmpty As Boolean
            Get
                Return Grid.Rows.Count = 0
            End Get
        End Property

        Public Sub Clear()
            Table = Nothing
            Grid.DataSource = Nothing
            Grid.Rows.Clear()
            Grid.ClearSelection()
            Count.Clear()
        End Sub

        ''' <summary>Shows <paramref name="rows"/> in place of whatever was shown before.</summary>
        Public Sub Show(rows As DataTable)
            Table = rows
            Grid.AutoGenerateColumns = False
            For i As Integer = 0 To _columns.Length - 1
                Grid.Columns(i).DataPropertyName = _columns(i)
            Next
            Grid.DataSource = rows
            ShowCount()
        End Sub

        ''' <summary>Adds rows that are not already shown (matched by id); shows them all if nothing is shown yet.</summary>
        Public Sub Merge(rows As DataTable)
            If Table Is Nothing Then
                Show(rows)
            Else
                AppendNewRows(Table, rows)
                ShowCount()
            End If
        End Sub

        Public Sub ShowCount()
            Count.Text = If(Table Is Nothing, "", Table.Rows.Count.ToString())
        End Sub

        Public Sub SelectLastRow(Optional column As Integer = 1)
            If IsEmpty Then Return
            Dim lastIndex = Grid.Rows.Count - 1
            Grid.ClearSelection()
            Grid.CurrentCell = Grid.Rows(lastIndex).Cells(column)
            Grid.Rows(lastIndex).Selected = True
            Grid.FirstDisplayedScrollingRowIndex = lastIndex
        End Sub

        ''' <summary>Values of one cell in each selected row, skipping blanks and values that do not parse.</summary>
        Public Function SelectedValues(Of T As Structure)(column As Integer, parse As Func(Of String, T?)) As List(Of T)
            Dim values As New List(Of T)
            For Each row As DataGridViewRow In Grid.SelectedRows
                Dim cell = row.Cells(column).Value
                If cell Is Nothing OrElse TypeOf cell Is DBNull Then Continue For
                Dim value = parse(cell.ToString())
                If value.HasValue Then values.Add(value.Value)
            Next
            Return values
        End Function

        Public Function SelectedIds() As List(Of Long)
            Return SelectedValues(0, Function(text)
                                          Dim id As Long
                                          Return If(Long.TryParse(text, id), id, CType(Nothing, Long?))
                                      End Function)
        End Function

        ''' <summary>Removes rows from the screen only; the database is not touched.</summary>
        Public Sub RemoveIds(ids As IEnumerable(Of Long))
            If Table Is Nothing Then Return
            Dim toRemove = New HashSet(Of Long)(ids)
            For Each row In Table.Rows.Cast(Of DataRow)().Where(Function(r) toRemove.Contains(Convert.ToInt64(r("id")))).ToList()
                Table.Rows.Remove(row)
            Next
            Table.AcceptChanges()
            ShowCount()
        End Sub

        ''' <summary>Empties the grid on screen only; the database is not touched.</summary>
        Public Sub ClearRows()
            If Table Is Nothing OrElse IsEmpty Then Return
            Table.Clear()
            Table.AcceptChanges()
            ShowCount()
        End Sub

        ''' <summary>Adds rows whose id is not already in the table.</summary>
        Public Shared Sub AppendNewRows(target As DataTable, rows As DataTable)
            Dim existingIds = New HashSet(Of Long)(target.Rows.Cast(Of DataRow)().Select(Function(r) Convert.ToInt64(r("id"))))
            For Each row As DataRow In rows.Rows
                If existingIds.Add(Convert.ToInt64(row("id"))) Then target.ImportRow(row)
            Next
            target.AcceptChanges()
        End Sub

    End Class

End Namespace
