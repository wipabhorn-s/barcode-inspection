Option Strict On

Imports System.Text
Imports Barcode_Inspection.Domain
Imports Npgsql

Namespace Data

    ''' <summary>Queries behind the Barcode Reconciliation tab.</summary>
    Public Class ReconciliationRepository

        Private Const Columns As String = "product_name, part_no, barcode, work_order, carton_box_no"

        Private ReadOnly _db As DbConnectionFactory

        Public Sub New(db As DbConnectionFactory)
            _db = db
        End Sub

        ''' <summary>Returns Nothing when the barcode has not passed final inspection.</summary>
        Public Async Function FindPassedAsync(barcode As String) As Task(Of PassedRecord)
            Dim rows = Await QueryAsync($"SELECT {Columns} FROM barcode_inspection.record_barcode_pass WHERE barcode = @barcode LIMIT 1",
                                        Sub(p) p.AddWithValue("@barcode", barcode)).ConfigureAwait(False)
            Return rows.FirstOrDefault()
        End Function

        Public Async Function GetCartonNumbersAsync(workOrder As String) As Task(Of List(Of Integer))
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand("SELECT DISTINCT carton_box_no FROM barcode_inspection.record_barcode_pass WHERE work_order = @wo ORDER BY carton_box_no", conn)
                    cmd.Parameters.AddWithValue("@wo", workOrder)
                    Dim cartons As New List(Of Integer)
                    Using reader = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                        While Await reader.ReadAsync().ConfigureAwait(False)
                            cartons.Add(reader.GetInt32(0))
                        End While
                    End Using
                    Return cartons
                End Using
            End Using
        End Function

        Public Function GetPassedAsync(filter As ReconciliationFilter) As Task(Of List(Of PassedRecord))
            Dim sql As New StringBuilder($"SELECT {Columns} FROM barcode_inspection.record_barcode_pass WHERE TRUE")
            If filter.WorkOrder IsNot Nothing Then sql.Append(" AND work_order = @wo")
            If filter.CartonNo.HasValue Then sql.Append(" AND carton_box_no = @carton")
            ' Carton numbers repeat across work orders, so without a work order the product narrows it down.
            If filter.WorkOrder Is Nothing Then sql.Append(" AND product_name = @product")
            sql.Append(" ORDER BY part_no ASC")

            Return QueryAsync(sql.ToString(),
                              Sub(p)
                                  If filter.WorkOrder IsNot Nothing Then p.AddWithValue("@wo", filter.WorkOrder)
                                  If filter.CartonNo.HasValue Then p.AddWithValue("@carton", filter.CartonNo.Value)
                                  If filter.WorkOrder Is Nothing Then p.AddWithValue("@product", filter.ProductName)
                              End Sub)
        End Function

        ''' <summary>Deletes passed records in a single statement.</summary>
        Public Async Function DeletePassedAsync(barcodes As IEnumerable(Of String)) As Task
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand("DELETE FROM barcode_inspection.record_barcode_pass WHERE barcode = ANY(@barcodes)", conn)
                    cmd.Parameters.AddWithValue("@barcodes", barcodes.ToArray())
                    Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                End Using
            End Using
        End Function

        Private Async Function QueryAsync(sql As String, addParameters As Action(Of NpgsqlParameterCollection)) As Task(Of List(Of PassedRecord))
            Dim records As New List(Of PassedRecord)
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(sql, conn)
                    addParameters(cmd.Parameters)
                    Using reader = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                        While Await reader.ReadAsync().ConfigureAwait(False)
                            records.Add(New PassedRecord With {
                                .ProductName = reader("product_name").ToString(),
                                .PartNo = If(IsDBNull(reader("part_no")), 0, Convert.ToInt32(reader("part_no"))),
                                .Barcode = reader("barcode").ToString().Trim(),
                                .WorkOrder = reader("work_order").ToString(),
                                .CartonNo = Convert.ToInt32(reader("carton_box_no"))
                            })
                        End While
                    End Using
                End Using
            End Using
            Return records
        End Function

    End Class

End Namespace
