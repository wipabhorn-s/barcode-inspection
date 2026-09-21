Option Strict On

Imports Npgsql

Namespace Data

    ''' <summary>A part that passed final inspection.</summary>
    Public Class PassedPart
        Public Property ProductName As String
        Public Property WorkOrder As String
    End Class

    Public Class CombinedEntry
        Public Property CartonNo As Integer
        Public Property ProductName As String
        Public Property WorkOrder As String
        Public Property Barcode As String
        Public Property OperatorId As String
    End Class

    ''' <summary>What the combined-carton service needs from the database.</summary>
    Public Interface ICombinedCartonRepository
        ''' <summary>Returns Nothing when the barcode has not passed final inspection.</summary>
        Function FindPassedPartAsync(barcode As String) As Task(Of PassedPart)
        Function IsCombinedAsync(barcode As String) As Task(Of Boolean)
        Function CountInCartonAsync(productName As String, cartonNo As Integer) As Task(Of Integer)
        ''' <summary>Returns False when the barcode was combined by someone else in the meantime.</summary>
        Function InsertAsync(entry As CombinedEntry) As Task(Of Boolean)
    End Interface

    Public Class CombinedCartonRepository
        Implements ICombinedCartonRepository

        Private Const GridColumns As String = "id, barcode, work_order, combined_carton_no, combined_date, combined_time::TIME(0) AS combined_time"

        Private ReadOnly _db As DbConnectionFactory

        Public Sub New(db As DbConnectionFactory)
            _db = db
        End Sub

        Public Function GetCartonAsync(productName As String, cartonNo As Integer) As Task(Of DataTable)
            Return QueryAsync($"SELECT {GridColumns} FROM barcode_inspection.record_combined_carton
                                WHERE product_name = @product_name AND combined_carton_no = @carton_no ORDER BY id ASC",
                              Sub(p)
                                  p.AddWithValue("@product_name", productName)
                                  p.AddWithValue("@carton_no", cartonNo)
                              End Sub)
        End Function

        Public Function GetLatestAsync(barcode As String) As Task(Of DataTable)
            Return QueryAsync($"SELECT {GridColumns} FROM barcode_inspection.record_combined_carton WHERE barcode = @barcode ORDER BY id DESC LIMIT 1",
                              Sub(p) p.AddWithValue("@barcode", barcode))
        End Function

        Public Async Function FindPassedPartAsync(barcode As String) As Task(Of PassedPart) Implements ICombinedCartonRepository.FindPassedPartAsync
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand("SELECT product_name, work_order FROM barcode_inspection.record_barcode_pass WHERE barcode = @barcode LIMIT 1", conn)
                    cmd.Parameters.AddWithValue("@barcode", barcode)
                    Using reader = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                        If Not Await reader.ReadAsync().ConfigureAwait(False) Then Return Nothing
                        Return New PassedPart With {.ProductName = reader("product_name").ToString(), .WorkOrder = reader("work_order").ToString()}
                    End Using
                End Using
            End Using
        End Function

        Public Async Function IsCombinedAsync(barcode As String) As Task(Of Boolean) Implements ICombinedCartonRepository.IsCombinedAsync
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM barcode_inspection.record_combined_carton WHERE barcode = @barcode)", conn)
                    cmd.Parameters.AddWithValue("@barcode", barcode)
                    Return Convert.ToBoolean(Await cmd.ExecuteScalarAsync().ConfigureAwait(False))
                End Using
            End Using
        End Function

        Public Async Function CountInCartonAsync(productName As String, cartonNo As Integer) As Task(Of Integer) Implements ICombinedCartonRepository.CountInCartonAsync
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand("SELECT COUNT(*) FROM barcode_inspection.record_combined_carton WHERE product_name = @product_name AND combined_carton_no = @carton_no", conn)
                    cmd.Parameters.AddWithValue("@product_name", productName)
                    cmd.Parameters.AddWithValue("@carton_no", cartonNo)
                    Return Convert.ToInt32(Await cmd.ExecuteScalarAsync().ConfigureAwait(False))
                End Using
            End Using
        End Function

        Public Async Function InsertAsync(entry As CombinedEntry) As Task(Of Boolean) Implements ICombinedCartonRepository.InsertAsync
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(
                    "INSERT INTO barcode_inspection.record_combined_carton (combined_carton_no, product_name, work_order, barcode, operator_id)
                     VALUES (@carton_no, @product_name, @work_order, @barcode, @operator_id)", conn)
                    cmd.Parameters.AddWithValue("@carton_no", entry.CartonNo)
                    cmd.Parameters.AddWithValue("@product_name", entry.ProductName)
                    cmd.Parameters.AddWithValue("@work_order", entry.WorkOrder)
                    cmd.Parameters.AddWithValue("@barcode", entry.Barcode)
                    cmd.Parameters.AddWithValue("@operator_id", entry.OperatorId)
                    Try
                        Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                        Return True
                    Catch ex As PostgresException When ex.SqlState = PostgresErrorCodes.UniqueViolation
                        Return False
                    End Try
                End Using
            End Using
        End Function

        Private Async Function QueryAsync(sql As String, addParameters As Action(Of NpgsqlParameterCollection)) As Task(Of DataTable)
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(sql, conn)
                    addParameters(cmd.Parameters)
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
