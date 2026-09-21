Option Strict On

Imports Barcode_Inspection.Domain
Imports Npgsql

Namespace Data

    ''' <summary>All SQL for the main barcode inspection screen lives here.</summary>
    Public Class InspectionRepository
        Implements IInspectionRepository

        ' Columns shown in the carton grid. Result flags are shown as 'PASS' or blank.
        Private Const PassRecordColumns As String =
            "part_no, barcode, barcode_hinge, work_order, carton_box_no, line_no, inspection_date, inspection_time::TIME(0) AS inspection_time,
             CASE WHEN dimension_result THEN 'PASS' END AS dimension_result,
             CASE WHEN electrical_result THEN 'PASS' END AS electrical_result,
             CASE WHEN appearance_result THEN 'PASS' END AS appearance_result,
             hinge_max_15, hinge_avg_15, hinge_max_120, hinge_avg_120, hinge_angle, hinge_vendor, hinge_date,
             CASE WHEN mc_1_result THEN 'PASS' END AS mc_1_result,
             CASE WHEN mc_2_result THEN 'PASS' END AS mc_2_result,
             remark"

        Private Const InsertColumns As String =
            "(product_name, work_order, barcode, carton_box_no, line_no, operator_id, electrical_result, dimension_result, appearance_result,
              barcode_hinge, hinge_max_15, hinge_avg_15, hinge_max_120, hinge_avg_120, hinge_angle, hinge_vendor, hinge_date,
              mc_1_result, mc_2_result, remark)
             VALUES (@ProductName, @WorkOrder, @Barcode, @Carton, @Line, @Operator, @ElectricalResult, @DimensionResult, @AppearanceResult,
              @Hinge, @HingeMax15, @HingeAvg15, @HingeMax120, @HingeAvg120, @HingeAngle, @HingeVendor, @HingeDate,
              @MC1Result, @MC2Result, @Remark)"

        Private ReadOnly _db As DbConnectionFactory

        Public Sub New(db As DbConnectionFactory)
            _db = db
        End Sub

        ''' <summary>Work orders that already have passed parts, most recently used first.</summary>
        Public Async Function GetWorkOrdersAsync(productName As String) As Task(Of List(Of String))
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(
                    "SELECT work_order FROM barcode_inspection.record_barcode_pass WHERE product_name = @product_name GROUP BY work_order ORDER BY MAX(id) DESC", conn)
                    cmd.Parameters.AddWithValue("@product_name", productName)
                    Dim workOrders As New List(Of String)
                    Using reader = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                        While Await reader.ReadAsync().ConfigureAwait(False)
                            If Not reader.IsDBNull(0) Then workOrders.Add(reader.GetString(0))
                        End While
                    End Using
                    Return workOrders
                End Using
            End Using
        End Function

        Public Async Function GetCartonRecordsAsync(productName As String, workOrder As String, cartonNo As Integer) As Task(Of DataTable)
            Dim sql = $"SELECT {PassRecordColumns}
                        FROM barcode_inspection.record_barcode_pass
                        WHERE product_name = @product_name AND work_order = @work_order AND carton_box_no = @carton
                        ORDER BY part_no ASC"

            Return Await QueryTableAsync(sql, Sub(p)
                                                  p.AddWithValue("@product_name", productName)
                                                  p.AddWithValue("@work_order", workOrder)
                                                  p.AddWithValue("@carton", cartonNo)
                                              End Sub).ConfigureAwait(False)
        End Function

        Public Async Function GetLatestPassRecordAsync(barcode As String) As Task(Of DataTable)
            Dim sql = $"SELECT {PassRecordColumns}
                        FROM barcode_inspection.record_barcode_pass
                        WHERE barcode = @barcode
                        ORDER BY id DESC
                        LIMIT 1"

            Return Await QueryTableAsync(sql, Sub(p) p.AddWithValue("@barcode", barcode)).ConfigureAwait(False)
        End Function

        Public Function PassRecordExistsAsync(barcode As String) As Task(Of Boolean) Implements IInspectionRepository.PassRecordExistsAsync
            Return ExistsAsync("SELECT EXISTS(SELECT 1 FROM barcode_inspection.record_barcode_pass WHERE barcode = @value)", barcode)
        End Function

        ''' <summary>True when a passed barcode starts with <paramref name="prefix"/>.</summary>
        Public Function PassRecordWithPrefixExistsAsync(prefix As String) As Task(Of Boolean) Implements IInspectionRepository.PassRecordWithPrefixExistsAsync
            Return ExistsAsync("SELECT EXISTS(SELECT 1 FROM barcode_inspection.record_barcode_pass WHERE barcode LIKE @value)", EscapeLike(prefix) & "%")
        End Function

        Public Function DimensionRecordExistsAsync(barcode As String) As Task(Of Boolean) Implements IInspectionRepository.DimensionRecordExistsAsync
            Return ExistsAsync("SELECT EXISTS(SELECT 1 FROM barcode_inspection.record_barcode_dimension WHERE barcode = @value)", barcode)
        End Function

        Public Function AppearanceRecordExistsAsync(barcode As String) As Task(Of Boolean) Implements IInspectionRepository.AppearanceRecordExistsAsync
            Return ExistsAsync("SELECT EXISTS(SELECT 1 FROM barcode_inspection.record_barcode_appearance WHERE barcode = @value)", barcode)
        End Function

        ''' <summary>Saves the inspection to the pass or the fail table, depending on its result.</summary>
        Public Async Function SaveAsync(inspection As InspectionData) As Task Implements IInspectionRepository.SaveAsync
            Dim table = If(inspection.IsPass, "record_barcode_pass", "record_barcode_fail")

            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand($"INSERT INTO barcode_inspection.{table} {InsertColumns}", conn)
                    AddInspectionParameters(cmd.Parameters, inspection)
                    Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                End Using
            End Using
        End Function

        Private Shared Sub AddInspectionParameters(p As NpgsqlParameterCollection, inspection As InspectionData)
            p.AddWithValue("@ProductName", inspection.ProductName)
            p.AddWithValue("@WorkOrder", inspection.WorkOrder)
            p.AddWithValue("@Barcode", inspection.Barcode)
            p.AddWithValue("@Carton", inspection.CartonNo)
            p.AddWithValue("@Line", inspection.LineNo)
            p.AddWithValue("@Operator", inspection.OperatorID)
            p.AddWithValue("@ElectricalResult", DbValue(inspection.ElectricalResult))
            p.AddWithValue("@DimensionResult", DbValue(inspection.DimensionResult))
            p.AddWithValue("@AppearanceResult", DbValue(inspection.AppearanceResult))
            p.AddWithValue("@Hinge", DbValue(inspection.Hinge))
            p.AddWithValue("@HingeMax15", DbValue(inspection.HingeMax15))
            p.AddWithValue("@HingeAvg15", DbValue(inspection.HingeAvg15))
            p.AddWithValue("@HingeMax120", DbValue(inspection.HingeMax120))
            p.AddWithValue("@HingeAvg120", DbValue(inspection.HingeAvg120))
            p.AddWithValue("@HingeAngle", DbValue(inspection.HingeAngle))
            p.AddWithValue("@HingeVendor", DbValue(inspection.HingeVendor))
            p.AddWithValue("@HingeDate", DbValue(inspection.HingeDate))
            p.AddWithValue("@MC1Result", DbValue(inspection.MC1Result))
            p.AddWithValue("@MC2Result", DbValue(inspection.MC2Result))
            p.AddWithValue("@Remark", DbValue(inspection.Remark))
        End Sub

        Private Async Function ExistsAsync(sql As String, value As String) As Task(Of Boolean)
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@value", value)
                    Return Convert.ToBoolean(Await cmd.ExecuteScalarAsync().ConfigureAwait(False))
                End Using
            End Using
        End Function

        Private Async Function QueryTableAsync(sql As String, addParameters As Action(Of NpgsqlParameterCollection)) As Task(Of DataTable)
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(sql, conn)
                    addParameters(cmd.Parameters)
                    ' DataAdapter.Fill has no async version; run it off the UI thread.
                    Return Await Task.Run(Function()
                                              Using adapter As New NpgsqlDataAdapter(cmd)
                                                  Dim table As New DataTable()
                                                  adapter.Fill(table)
                                                  Return table
                                              End Using
                                          End Function).ConfigureAwait(False)
                End Using
            End Using
        End Function

        Private Shared Function DbValue(Of T As Structure)(value As T?) As Object
            Return If(value.HasValue, CObj(value.Value), DBNull.Value)
        End Function

        Private Shared Function DbValue(value As String) As Object
            Return If(String.IsNullOrEmpty(value), DBNull.Value, CObj(value))
        End Function

        ''' <summary>Stops % and _ inside a barcode from acting as LIKE wildcards.</summary>
        Private Shared Function EscapeLike(value As String) As String
            Return value.Replace("\", "\\").Replace("%", "\%").Replace("_", "\_")
        End Function

    End Class

End Namespace
