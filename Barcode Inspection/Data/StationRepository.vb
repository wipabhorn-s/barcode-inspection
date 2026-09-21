Option Strict On

Imports System.Text
Imports Barcode_Inspection.Domain
Imports Npgsql

Namespace Data

    ''' <summary>What the station inspection service needs from the database.</summary>
    Public Interface IStationRepository
        Function ExistsAsync(barcode As String) As Task(Of Boolean)
        Function InsertAsync(inspection As InspectionData) As Task
    End Interface

    ''' <summary>
    ''' SQL for one station's table (dimension or appearance). The same class serves both stations;
    ''' only the table and result column change, and those come from <see cref="StationInfo"/>.
    ''' </summary>
    Public Class StationRepository
        Implements IStationRepository

        Private ReadOnly _db As DbConnectionFactory
        Private ReadOnly _info As StationInfo

        Public Sub New(db As DbConnectionFactory, info As StationInfo)
            _db = db
            _info = info
        End Sub

        Private ReadOnly Property QualifiedTable As String
            Get
                Return "barcode_inspection." & _info.TableName
            End Get
        End Property

        ''' <summary>'PASS' / 'FAIL' text shown in the grids.</summary>
        Private ReadOnly Property ResultText As String
            Get
                Return $"CASE WHEN {_info.ResultColumn} THEN 'PASS' ELSE 'FAIL' END AS {_info.ResultColumn}"
            End Get
        End Property

        Public Async Function GetWorkOrdersAsync(productName As String) As Task(Of List(Of String))
            Dim sql = $"SELECT work_order FROM {QualifiedTable} WHERE product_name = @product_name GROUP BY work_order ORDER BY MAX(id) DESC"
            Dim result = Await QueryAsync(sql, New Dictionary(Of String, Object) From {{"product_name", productName}}).ConfigureAwait(False)

            Return result.Rows.Cast(Of DataRow)().
                Where(Function(r) Not IsDBNull(r("work_order"))).
                Select(Function(r) r("work_order").ToString()).
                ToList()
        End Function

        Public Async Function CountAsync(productName As String, workOrder As String) As Task(Of Integer)
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand($"SELECT COUNT(*) FROM {QualifiedTable} WHERE product_name = @product_name AND work_order = @work_order", conn)
                    cmd.Parameters.AddWithValue("@product_name", productName)
                    cmd.Parameters.AddWithValue("@work_order", workOrder)
                    Return Convert.ToInt32(Await cmd.ExecuteScalarAsync().ConfigureAwait(False))
                End Using
            End Using
        End Function

        ''' <summary>The last <paramref name="limit"/> parts of a work order, oldest first.</summary>
        Public Function GetRecentRecordsAsync(productName As String, workOrder As String, Optional limit As Integer = 100) As Task(Of DataTable)
            Dim sql = $"SELECT * FROM (
                            SELECT part_no, barcode, barcode_hinge, work_order, line_no, inspection_date,
                                   inspection_time::TIME(0) AS inspection_time, {ResultText}, remark
                            FROM {QualifiedTable}
                            WHERE product_name = @product_name AND work_order = @work_order
                            ORDER BY part_no DESC
                            LIMIT {limit}) recent
                        ORDER BY part_no ASC"

            Return QueryAsync(sql, New Dictionary(Of String, Object) From {
                {"product_name", productName},
                {"work_order", workOrder}
            })
        End Function

        Public Function GetLatestRecordAsync(barcode As String) As Task(Of DataTable)
            Dim sql = $"SELECT part_no, barcode, barcode_hinge, work_order, line_no, inspection_date,
                               inspection_time::TIME(0) AS inspection_time, {ResultText}, remark
                        FROM {QualifiedTable}
                        WHERE barcode = @barcode
                        ORDER BY id DESC
                        LIMIT 1"

            Return QueryAsync(sql, New Dictionary(Of String, Object) From {{"barcode", barcode}})
        End Function

        Public Async Function ExistsAsync(barcode As String) As Task(Of Boolean) Implements IStationRepository.ExistsAsync
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand($"SELECT EXISTS(SELECT 1 FROM {QualifiedTable} WHERE barcode = @barcode)", conn)
                    cmd.Parameters.AddWithValue("@barcode", barcode)
                    Return Convert.ToBoolean(Await cmd.ExecuteScalarAsync().ConfigureAwait(False))
                End Using
            End Using
        End Function

        Public Async Function InsertAsync(inspection As InspectionData) As Task Implements IStationRepository.InsertAsync
            Dim sql = $"INSERT INTO {QualifiedTable} (product_name, work_order, barcode, barcode_hinge, line_no, operator_id, {_info.ResultColumn}, remark)
                        VALUES (@product_name, @work_order, @barcode, @hinge, @line_no, @operator_id, @result, @remark)"

            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@product_name", inspection.ProductName)
                    cmd.Parameters.AddWithValue("@work_order", inspection.WorkOrder)
                    cmd.Parameters.AddWithValue("@barcode", inspection.Barcode)
                    cmd.Parameters.AddWithValue("@hinge", If(String.IsNullOrEmpty(inspection.Hinge), DBNull.Value, CObj(inspection.Hinge)))
                    cmd.Parameters.AddWithValue("@line_no", inspection.LineNo)
                    cmd.Parameters.AddWithValue("@operator_id", inspection.OperatorID)
                    cmd.Parameters.AddWithValue("@result", inspection.IsPass)
                    cmd.Parameters.AddWithValue("@remark", If(String.IsNullOrWhiteSpace(inspection.Remark), DBNull.Value, CObj(inspection.Remark)))
                    Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                End Using
            End Using
        End Function

        ''' <summary>Deletes every record of the given barcodes in a single (atomic) statement. Returns the number of barcodes.</summary>
        Public Function DeleteByBarcodesAsync(barcodes As IEnumerable(Of String)) As Task(Of Integer)
            Return DeleteWhereAsync("barcode = ANY(@values)", barcodes.ToArray())
        End Function

        Public Function DeleteByIdsAsync(ids As IEnumerable(Of Long)) As Task(Of Integer)
            Return DeleteWhereAsync("id = ANY(@values)", ids.ToArray())
        End Function

        Private Async Function DeleteWhereAsync(condition As String, values As Array) As Task(Of Integer)
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand($"DELETE FROM {QualifiedTable} WHERE {condition}", conn)
                    cmd.Parameters.AddWithValue("@values", values)
                    Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                    Return values.Length
                End Using
            End Using
        End Function

        ''' <summary>Passed (or failed) records matching the filter, for the report tab.</summary>
        Public Function SearchAsync(filter As StationSearchFilter, passed As Boolean) As Task(Of DataTable)
            Dim query = BuildSearchQuery(_info, filter, passed)
            Return QueryAsync(query.Sql, query.Parameters)
        End Function

        ''' <summary>Builds the report query. Values always go in parameters; only whitelisted names go in the SQL text.</summary>
        Public Shared Function BuildSearchQuery(info As StationInfo, filter As StationSearchFilter, passed As Boolean) As (Sql As String, Parameters As Dictionary(Of String, Object))
            Dim parameters As New Dictionary(Of String, Object)
            Dim sql As New StringBuilder()

            sql.AppendLine($"SELECT id, part_no, barcode, barcode_hinge, product_name, work_order, line_no, operator_id, inspection_date,")
            sql.AppendLine($"       inspection_time::TIME(0) AS inspection_time, CASE WHEN {info.ResultColumn} THEN 'PASS' ELSE 'FAIL' END AS {info.ResultColumn}, remark")
            sql.AppendLine($"FROM barcode_inspection.{info.TableName}")
            sql.AppendLine($"WHERE {info.ResultColumn} = {If(passed, "TRUE", "FALSE")}")

            If filter.ProductName IsNot Nothing Then
                sql.AppendLine("AND product_name = @ProductName")
                parameters("ProductName") = filter.ProductName
            End If

            If filter.WorkOrder IsNot Nothing Then
                sql.AppendLine("AND work_order = @WorkOrder")
                parameters("WorkOrder") = filter.WorkOrder
            End If

            If filter.DateFrom.HasValue AndAlso filter.DateTo.HasValue Then
                Dim first = filter.DateFrom.Value.Date
                Dim last = filter.DateTo.Value.Date
                sql.AppendLine("AND inspection_date BETWEEN @DateFrom AND @DateTo")
                parameters("DateFrom") = If(first <= last, first, last)
                parameters("DateTo") = If(first <= last, last, first)
            End If

            If filter.Barcode IsNot Nothing Then
                sql.AppendLine("AND barcode = @Barcode")
                parameters("Barcode") = filter.Barcode
            End If

            If filter.Hinge IsNot Nothing Then
                sql.AppendLine("AND barcode_hinge = @Hinge")
                parameters("Hinge") = filter.Hinge
            End If

            sql.AppendLine("ORDER BY part_no ASC")
            Return (sql.ToString(), parameters)
        End Function

        Private Async Function QueryAsync(sql As String, parameters As Dictionary(Of String, Object)) As Task(Of DataTable)
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(sql, conn)
                    For Each p In parameters
                        cmd.Parameters.AddWithValue("@" & p.Key, p.Value)
                    Next
                    ' DataAdapter.Fill has no async version; run it off the UI thread.
                    Return Await Task.Run(Function()
                                              Using adapter As New NpgsqlDataAdapter(cmd)
                                                  Dim result As New DataTable()
                                                  adapter.Fill(result)
                                                  Return result
                                              End Using
                                          End Function).ConfigureAwait(False)
                End Using
            End Using
        End Function

    End Class

End Namespace
