Option Strict On

Imports System.Text
Imports Npgsql

Namespace Data

    ''' <summary>Search conditions for the Final Data tab. Nothing means "any".</summary>
    Public Class FinalReportFilter
        Public Property ProductName As String
        Public Property WorkOrder As String
        Public Property CartonNo As Integer?
        Public Property DateFrom As Date?
        Public Property DateTo As Date?
        Public Property Barcode As String
        Public Property Hinge As String
        Public Property CombinedCartonNo As Integer?
    End Class

    ''' <summary>A combined carton and the work orders packed into it, e.g. "12 (WO1, WO2)".</summary>
    Public Class CombinedCartonOption
        Public Property CartonNo As Integer
        Public Property WorkOrders As String

        Public Overrides Function ToString() As String
            Return $"{CartonNo} ({WorkOrders})"
        End Function
    End Class

    ''' <summary>Queries behind the Final Data tab (final inspection results).</summary>
    Public Class FinalReportRepository

        ''' <summary>The fail grid is loaded per product; cap it so a busy product cannot freeze the screen.</summary>
        Public Const FailRowLimit As Integer = 50000

        Private ReadOnly _db As DbConnectionFactory

        Public Sub New(db As DbConnectionFactory)
            _db = db
        End Sub

        ''' <summary>Same list as the Recorder tab uses.</summary>
        Public Function GetWorkOrdersAsync(productName As String) As Task(Of List(Of String))
            Return New InspectionRepository(_db).GetWorkOrdersAsync(productName)
        End Function

        Public Async Function GetCombinedCartonsAsync(productName As String) As Task(Of List(Of CombinedCartonOption))
            Dim rows = Await QueryAsync(
                "SELECT combined_carton_no, STRING_AGG(DISTINCT work_order, ', ' ORDER BY work_order) AS wo_list
                 FROM barcode_inspection.record_combined_carton
                 WHERE product_name = @product_name
                 GROUP BY combined_carton_no
                 ORDER BY combined_carton_no",
                New Dictionary(Of String, Object) From {{"product_name", productName}}).ConfigureAwait(False)

            Return rows.Rows.Cast(Of DataRow)().Select(Function(r) New CombinedCartonOption With {
                .CartonNo = Convert.ToInt32(r("combined_carton_no")),
                .WorkOrders = r("wo_list").ToString()
            }).ToList()
        End Function

        ''' <summary>Passed parts matching the filter, with their combined-carton details when they have any.</summary>
        Public Function SearchPassAsync(filter As FinalReportFilter) As Task(Of DataTable)
            Dim query = BuildPassQuery(filter)
            Return QueryAsync(query.Sql, query.Parameters)
        End Function

        ''' <summary>All failed scans of a product (the "Defect" option), oldest first.</summary>
        Public Function GetFailuresAsync(productName As String) As Task(Of DataTable)
            Dim sql = $"SELECT id, part_no, barcode, barcode_hinge, product_name, work_order, carton_box_no, line_no, operator_id,
                               inspection_date, inspection_time::TIME(0) AS inspection_time,
                               {PassFailText("dimension_result")}, {PassFailText("electrical_result")}, {PassFailText("appearance_result")},
                               hinge_max_15, hinge_avg_15, hinge_max_120, hinge_avg_120, hinge_angle, hinge_vendor, hinge_date,
                               {PassFailText("mc_1_result")}, {PassFailText("mc_2_result")}, remark
                        FROM barcode_inspection.record_barcode_fail
                        WHERE product_name = @ProductName
                        ORDER BY id ASC
                        LIMIT {FailRowLimit}"

            Return QueryAsync(sql, New Dictionary(Of String, Object) From {{"ProductName", productName}})
        End Function

        Public Function DeletePassByIdsAsync(ids As IEnumerable(Of Long)) As Task
            Return ExecuteAsync("DELETE FROM barcode_inspection.record_barcode_pass WHERE id = ANY(@values)", ids.ToArray())
        End Function

        Public Function DeleteFailByIdsAsync(ids As IEnumerable(Of Long)) As Task
            Return ExecuteAsync("DELETE FROM barcode_inspection.record_barcode_fail WHERE id = ANY(@values)", ids.ToArray())
        End Function

        ''' <summary>Takes parts out of their combined cartons. The pass records themselves are kept.</summary>
        Public Function DeleteCombinedAsync(barcodes As IEnumerable(Of String)) As Task
            Return ExecuteAsync("DELETE FROM barcode_inspection.record_combined_carton WHERE barcode = ANY(@values)", barcodes.ToArray())
        End Function

        Public Shared Function BuildPassQuery(filter As FinalReportFilter) As (Sql As String, Parameters As Dictionary(Of String, Object))
            Dim parameters As New Dictionary(Of String, Object)
            Dim sql As New StringBuilder(
                "SELECT p.id, p.part_no, p.barcode, p.barcode_hinge, p.product_name, p.work_order,
                        p.carton_box_no, p.line_no, p.operator_id, p.inspection_date,
                        p.inspection_time::TIME(0) AS inspection_time,
                        CASE WHEN p.dimension_result IS TRUE THEN 'PASS' END AS dimension_result,
                        CASE WHEN p.electrical_result IS TRUE THEN 'PASS' END AS electrical_result,
                        CASE WHEN p.appearance_result IS TRUE THEN 'PASS' END AS appearance_result,
                        p.hinge_max_15, p.hinge_avg_15, p.hinge_max_120, p.hinge_avg_120,
                        p.hinge_angle, p.hinge_vendor, p.hinge_date,
                        CASE WHEN p.mc_1_result IS TRUE THEN 'PASS' END AS mc_1_result,
                        CASE WHEN p.mc_2_result IS TRUE THEN 'PASS' END AS mc_2_result, p.remark,
                        c.combined_carton_no, c.operator_id AS combined_operator_id,
                        c.combined_date, c.combined_time::TIME(0) AS combined_time
                 FROM barcode_inspection.record_barcode_pass p
                 LEFT JOIN barcode_inspection.record_combined_carton c ON c.barcode = p.barcode
                 WHERE TRUE" & vbCrLf)

            Dim addCondition = Sub(condition As String, name As String, value As Object)
                                   sql.AppendLine($"AND {condition}")
                                   parameters(name) = value
                               End Sub

            If filter.CombinedCartonNo.HasValue Then addCondition("c.combined_carton_no = @CombinedCartonNo", "CombinedCartonNo", filter.CombinedCartonNo.Value)
            If filter.ProductName IsNot Nothing Then addCondition("p.product_name = @ProductName", "ProductName", filter.ProductName)
            If filter.WorkOrder IsNot Nothing Then addCondition("p.work_order = @WorkOrder", "WorkOrder", filter.WorkOrder)
            If filter.CartonNo.HasValue Then addCondition("p.carton_box_no = @CartonNo", "CartonNo", filter.CartonNo.Value)

            If filter.DateFrom.HasValue AndAlso filter.DateTo.HasValue Then
                Dim first = filter.DateFrom.Value.Date
                Dim last = filter.DateTo.Value.Date
                sql.AppendLine("AND p.inspection_date BETWEEN @DateFrom AND @DateTo")
                parameters("DateFrom") = If(first <= last, first, last)
                parameters("DateTo") = If(first <= last, last, first)
            End If

            If filter.Barcode IsNot Nothing Then addCondition("p.barcode = @Barcode", "Barcode", filter.Barcode)
            If filter.Hinge IsNot Nothing Then addCondition("p.barcode_hinge = @BarcodeHinge", "BarcodeHinge", filter.Hinge)

            sql.AppendLine("ORDER BY p.inspection_date ASC, p.id ASC")
            Return (sql.ToString(), parameters)
        End Function

        Private Shared Function PassFailText(column As String) As String
            Return $"CASE WHEN {column} IS TRUE THEN 'PASS' WHEN {column} IS FALSE THEN 'FAIL' END AS {column}"
        End Function

        Private Async Function ExecuteAsync(sql As String, values As Array) As Task
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@values", values)
                    Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                End Using
            End Using
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
