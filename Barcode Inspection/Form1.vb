Imports System.IO
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services
Imports Barcode_Inspection.UI
Imports Npgsql

Public Class Form1
    Private WithEvents db As DbConnectionFactory

    Private runningCommand As Npgsql.NpgsqlCommand = Nothing

    Private Async Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Try
            db = DbConnectionFactory.FromConfiguration()
        Catch ex As Configuration.ConfigurationErrorsException
            MessageBox.Show(ex.Message, "Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Close()
            Return
        End Try
        AdminPasswordService.Current = New AdminPasswordService(New AppSettingRepository(db))
        LoadSettings()
        CreateControllers()
        LoadWorkOrderToCheckedListBox()
        Await settingsTab.LoadProductsAsync()

        ToolTip1.SetToolTip(Button23, "Run SQL query")
        ToolTip1.SetToolTip(Button24, "Cancel current query execution.")
        ToolTip1.SetToolTip(Button25, "Clear SQL command text and display.")
    End Sub

    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        NpgsqlConnection.ClearAllPools()
    End Sub

    Private Sub UpdateConnectionStatus(isConnected As Boolean)
        If isConnected Then
            ToolStripButton1.Text = "Connected"
            ToolStripButton1.BackColor = Color.FromArgb(204, 255, 229)
        Else
            ToolStripButton1.Text = "Disconnected"
            ToolStripButton1.BackColor = Color.FromArgb(255, 204, 229)
        End If
    End Sub

    ' Kept for the other tabs, which still open connections themselves.
    Private Function GetConnection(Optional maxRetries As Integer = 3) As NpgsqlConnection
        Return db.TryOpenConnection(maxRetries)
    End Function

    Private Sub Db_ConnectionStatusChanged(isConnected As Boolean) Handles db.ConnectionStatusChanged
        ' Raised from background threads too, so switch to the UI thread before touching controls.
        If InvokeRequired Then
            BeginInvoke(New Action(Of Boolean)(AddressOf UpdateConnectionStatus), isConnected)
        Else
            UpdateConnectionStatus(isConnected)
        End If
    End Sub

    Private Sub ToolStripButton1_Click(sender As Object, e As EventArgs) Handles ToolStripButton1.Click
        cboWorkOrder.Items.Clear()
        cboWorkOrder.SelectedIndex = -1
        cboWorkOrder.Text = ""
        ComboBox4.Items.Clear()
        ComboBox4.SelectedIndex = -1
        ComboBox4.Text = ""

        Using conn = GetConnection(maxRetries:=2)
            If conn Is Nothing Then
                MessageBox.Show("Cannot connect to database.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If

            Try
                Using cmd As New NpgsqlCommand(
                    "DELETE FROM barcode_inspection.pass_part_counter
                    WHERE NOT EXISTS (SELECT 1 FROM barcode_inspection.record_barcode_pass r WHERE r.work_order = pass_part_counter.work_order);
     
                    DELETE FROM barcode_inspection.fail_part_counter
                    WHERE NOT EXISTS (SELECT 1 FROM barcode_inspection.record_barcode_fail r WHERE r.work_order = fail_part_counter.work_order);
     
                    DELETE FROM barcode_inspection.dimension_part_counter
                    WHERE NOT EXISTS (SELECT 1 FROM barcode_inspection.record_barcode_dimension r WHERE r.work_order = dimension_part_counter.work_order);
     
                    DELETE FROM barcode_inspection.appearance_part_counter
                    WHERE NOT EXISTS (SELECT 1 FROM barcode_inspection.record_barcode_appearance r WHERE r.work_order = appearance_part_counter.work_order);", conn)
                    cmd.ExecuteNonQuery()
                End Using
            Catch ex As Exception
                Debug.WriteLine($"Clear counter error: {ex.Message}")
            End Try
        End Using

        LoadWorkOrderToCheckedListBox()
    End Sub

    Private Sub CellPainting(sender As Object, e As DataGridViewCellPaintingEventArgs) Handles dgvCartonRecords.CellPainting, DataGridView2.CellPainting, DataGridView3.CellPainting, DataGridView4.CellPainting, DataGridView7.CellPainting, DataGridView8.CellPainting, DataGridView6.CellPainting, DataGridView9.CellPainting, DataGridView10.CellPainting, DataGridView12.CellPainting, DataGridView11.CellPainting
        If e.RowIndex >= 0 AndAlso e.ColumnIndex >= 0 Then
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All And Not DataGridViewPaintParts.Focus)
            e.Handled = True
        End If
    End Sub

    Private Sub LoadSettings()
        DateTimePicker1.Value = DateTime.Now
        DateTimePicker2.Value = DateTime.Now
        DateTimePicker3.Value = DateTime.Now
        DateTimePicker4.Value = DateTime.Now
        DateTimePicker5.Value = DateTime.Now
        DateTimePicker6.Value = DateTime.Now
    End Sub

    Private Sub TabControl1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles TabControl1.SelectedIndexChanged
        If TabControl1.SelectedTab Is TabPage2 Then
            settingsTab.Lock()
        End If

        If TabControl1.SelectedTab Is TabPage4 Then
            LockQueryTab()
        End If
    End Sub

    Private Sub LockQueryTab()
        Button23.Enabled = False
        Button24.Enabled = False
        Button25.Enabled = False
        Button26.Enabled = False
        Button27.Enabled = False
        Button28.Enabled = False
        RichTextBox1.Enabled = False

        Label25.Visible = True
        TextBox27.Visible = True
        Button20.Visible = True

        TextBox27.Clear()
        TextBox27.Focus()
    End Sub

    Private Sub UnlockQueryTab()
        Button23.Enabled = True
        Button24.Enabled = True
        Button25.Enabled = True
        Button26.Enabled = True
        Button27.Enabled = True
        Button28.Enabled = True
        RichTextBox1.Enabled = True

        Label25.Visible = False
        TextBox27.Visible = False
        Button20.Visible = False
    End Sub

    Private Async Sub Button20_Click(sender As Object, e As EventArgs) Handles Button20.Click
        Button20.Enabled = False
        Dim correct = Await UiKit.CheckAdminPasswordAsync(TextBox27.Text)
        Button20.Enabled = True

        If correct Then
            UnlockQueryTab()
            RichTextBox1.SelectAll()
            RichTextBox1.Focus()
        Else
            TextBox27.Clear()
            TextBox27.Focus()
        End If
    End Sub

    Private Sub TextBox27_KeyDown(sender As Object, e As KeyEventArgs) Handles TextBox27.KeyDown
        If e.KeyCode = Keys.Enter Then
            Button20.PerformClick()
            e.SuppressKeyPress = True
        End If
    End Sub

    Public Class WorkOrderItem
        Public Property ProductName As String
        Public Property WorkOrder As String
        Public Property Qty As Integer

        Public Overrides Function ToString() As String
            Return $"{ProductName} | {WorkOrder} ({Qty})"
        End Function
    End Class

    Private Sub LoadWorkOrderToCheckedListBox()
        CheckedListBox1.Items.Clear()

        Dim sql As String = "SELECT product_name, work_order, COUNT(*) AS qty " & "FROM barcode_inspection.record_barcode_pass " & "GROUP BY product_name, work_order " & "ORDER BY product_name, work_order;"

        Using conn = GetConnection()
            If conn Is Nothing Then
                MessageBox.Show("Cannot connect to database.")
                Exit Sub
            End If

            Using cmd As New NpgsqlCommand(sql, conn)
                Using r = cmd.ExecuteReader()
                    While r.Read()

                        Dim item As New WorkOrderItem With {
                            .ProductName = r("product_name").ToString(),
                            .WorkOrder = r("work_order").ToString(),
                            .Qty = Convert.ToInt32(r("qty"))
                        }

                        CheckedListBox1.Items.Add(item, False)
                    End While
                End Using
            End Using
        End Using
    End Sub

    Private Sub Button21_Click(sender As Object, e As EventArgs) Handles Button21.Click
        If CheckedListBox1.CheckedItems.Count = 0 Then
            MessageBox.Show("Please select at least one Work Order to export.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim selectedItems As New List(Of WorkOrderItem)

        For Each it As WorkOrderItem In CheckedListBox1.CheckedItems
            selectedItems.Add(it)
        Next

        Using sfd As New SaveFileDialog()
            sfd.Filter = "CSV file (*.csv)|*.csv"
            sfd.FileName = $"Barcode_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"

            If sfd.ShowDialog() <> DialogResult.OK Then Return

            Dim filePath As String = sfd.FileName

            Try
                ExportBarcodeAllColumnToCsv(filePath, selectedItems)
                MessageBox.Show("Export completed successfully.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information)

            Catch ex As Exception
                MessageBox.Show(ex.Message, "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    Private Sub ExportBarcodeAllColumnToCsv(filePath As String, selectedItems As List(Of WorkOrderItem))
        Dim keys As New List(Of String)
        For Each it In selectedItems
            keys.Add($"{it.ProductName}|{it.WorkOrder}")
        Next

        Using conn = GetConnection()
            If conn Is Nothing Then
                Throw New Exception("Cannot connect to database.")
            End If

            Using cmd As New Npgsql.NpgsqlCommand()
                cmd.Connection = conn

                Dim sql As New Text.StringBuilder()

                sql.AppendLine("SELECT")
                sql.AppendLine("    t.id,")
                sql.AppendLine("    t.product_name,")
                sql.AppendLine("    t.work_order,")
                sql.AppendLine("    t.part_no,")
                sql.AppendLine("    t.barcode,")
                sql.AppendLine("    t.carton_box_no,")
                sql.AppendLine("    t.line_no,")
                sql.AppendLine("    t.operator_id,")

                sql.AppendLine("    t.inspection_date::date AS inspection_date,")
                sql.AppendLine("    t.inspection_time,")

                sql.AppendLine("    CASE WHEN t.electrical_result = true  THEN 'PASS'")
                sql.AppendLine("         WHEN t.electrical_result = false THEN 'FAIL'")
                sql.AppendLine("         ELSE NULL END AS electrical_result,")

                sql.AppendLine("    CASE WHEN t.dimension_result = true  THEN 'PASS'")
                sql.AppendLine("         WHEN t.dimension_result = false THEN 'FAIL'")
                sql.AppendLine("         ELSE NULL END AS dimension_result,")

                sql.AppendLine("    CASE WHEN t.appearance_result = true  THEN 'PASS'")
                sql.AppendLine("         WHEN t.appearance_result = false THEN 'FAIL'")
                sql.AppendLine("         ELSE NULL END AS appearance_result,")

                sql.AppendLine("    CASE WHEN t.mc_1_result = true  THEN 'PASS'")
                sql.AppendLine("         WHEN t.mc_1_result = false THEN 'FAIL'")
                sql.AppendLine("         ELSE NULL END AS mc_1_result,")

                sql.AppendLine("    CASE WHEN t.mc_2_result = true  THEN 'PASS'")
                sql.AppendLine("         WHEN t.mc_2_result = false THEN 'FAIL'")
                sql.AppendLine("         ELSE NULL END AS mc_2_result,")

                sql.AppendLine("    t.barcode_hinge,")
                sql.AppendLine("    t.hinge_max_15,")
                sql.AppendLine("    t.hinge_avg_15,")
                sql.AppendLine("    t.hinge_max_120,")
                sql.AppendLine("    t.hinge_avg_120,")
                sql.AppendLine("    t.hinge_angle,")
                sql.AppendLine("    t.hinge_vendor,")
                sql.AppendLine("    t.hinge_date")

                sql.AppendLine("FROM barcode_inspection.record_barcode_pass t")
                sql.AppendLine("WHERE (t.product_name || '|' || t.work_order) = ANY(@keys)")
                sql.AppendLine("ORDER BY t.product_name, t.work_order, t.carton_box_no")

                cmd.CommandText = sql.ToString()
                cmd.Parameters.AddWithValue("@keys", keys.ToArray())

                Using writer As New IO.StreamWriter(filePath, False, New System.Text.UTF8Encoding(True))

                    Using reader = cmd.ExecuteReader()
                        Dim header As New List(Of String)

                        For i As Integer = 0 To reader.FieldCount - 1
                            header.Add(reader.GetName(i))
                        Next

                        writer.WriteLine(String.Join(",", header))

                        While reader.Read()
                            Dim values As New List(Of String)

                            For i As Integer = 0 To reader.FieldCount - 1
                                Dim v = reader(i)

                                If v Is DBNull.Value Then
                                    values.Add("")
                                Else
                                    Dim s As String

                                    If TypeOf v Is DateTime Then
                                        s = CType(v, DateTime).ToString("dd-MMM-yyyy")
                                    Else
                                        s = v.ToString()
                                    End If

                                    s = s.Replace("""", """""")
                                    If s.Contains(",") OrElse s.Contains("""") OrElse s.Contains(vbCr) Then
                                        s = $"""{s}"""
                                    End If

                                    values.Add(s)
                                End If
                            Next

                            writer.WriteLine(String.Join(",", values))
                        End While
                    End Using
                End Using
            End Using
        End Using
    End Sub

    Private Sub Button22_Click(sender As Object, e As EventArgs) Handles Button22.Click
        For i As Integer = 0 To CheckedListBox1.Items.Count - 1
            CheckedListBox1.SetItemChecked(i, False)
        Next
    End Sub

    Private Sub Button23_Click(sender As Object, e As EventArgs) Handles Button23.Click

        Dim sqlText As String = If(
            RichTextBox1.SelectionLength > 0,
            RichTextBox1.SelectedText.Trim(),
            RichTextBox1.Text.Trim()
        )

        If String.IsNullOrWhiteSpace(sqlText) Then
            MessageBox.Show("Please enter SQL command.", "No SQL", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        If RichTextBox1.SelectionLength = 0 Then
            Dim fullText = RichTextBox1.Text.ToUpper()
            Dim hasSelect = fullText.Contains("SELECT")
            Dim hasUpdate = fullText.Contains("UPDATE")
            If hasSelect AndAlso hasUpdate Then
                MessageBox.Show(
                "This template contains multiple statements." & vbCrLf &
                "Please highlight the statement you want to run first.",
                "Selection Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning)
                Return
            End If
        End If

        Dim firstWord As String = ""
        For Each line As String In sqlText.Split({vbCrLf, vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim trimmed = line.Trim()
            If Not trimmed.StartsWith("--") Then
                firstWord = trimmed.Split({" "c, vbTab}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                Exit For
            End If
        Next

        Dim isSelect As Boolean = String.Equals(firstWord, "SELECT", StringComparison.OrdinalIgnoreCase)
        Dim isUpdate As Boolean = String.Equals(firstWord, "UPDATE", StringComparison.OrdinalIgnoreCase)

        If Not isSelect AndAlso Not isUpdate Then
            MessageBox.Show("Only SELECT or UPDATE statements are allowed.", "SQL Blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        If isUpdate AndAlso sqlText.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) < 0 Then
            MessageBox.Show("UPDATE without WHERE clause is not allowed.", "SQL Blocked", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        DataGridView5.DataSource = Nothing
        DataGridView5.Rows.Clear()
        DataGridView5.Columns.Clear()

        Try
            Using conn = GetConnection()
                If conn Is Nothing Then
                    MessageBox.Show("Cannot connect to database.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return
                End If

                Using cmd As New Npgsql.NpgsqlCommand(sqlText, conn)
                    runningCommand = cmd
                    cmd.CommandTimeout = 0

                    If isSelect Then
                        Using da As New Npgsql.NpgsqlDataAdapter(cmd)
                            Dim dt As New DataTable()
                            da.Fill(dt)
                            DataGridView5.DataSource = dt
                        End Using
                    Else
                        Dim affected = cmd.ExecuteNonQuery()
                        MessageBox.Show($"UPDATE completed. {affected} row(s) affected.", "Update Success", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    End If
                End Using

                runningCommand = Nothing
            End Using

        Catch ex As Exception
            MessageBox.Show(ex.Message, "SQL Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub Button24_Click(sender As Object, e As EventArgs) Handles Button24.Click
        If runningCommand Is Nothing Then
            MessageBox.Show("No query is currently running.", "Stop Query", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Try
            runningCommand.Cancel()
            MessageBox.Show("Query execution cancelled.", "Stop Query", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show(ex.Message, "Cancel Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub Button25_Click(sender As Object, e As EventArgs) Handles Button25.Click
        RichTextBox1.Clear()

        DataGridView5.DataSource = Nothing
        DataGridView5.Rows.Clear()
        DataGridView5.Columns.Clear()
        RichTextBox1.Focus()
    End Sub

    Private Sub Button26_Click(sender As Object, e As EventArgs) Handles Button26.Click
        Dim template As String =
        "-- ============================================" & vbCrLf &
        "-- Case 1: WO ผิด → WO ใหม่ที่ไม่เคยมีใน database มาก่อน" & vbCrLf &
        "-- แก้ wo จากนั้นคลุมแล้วกด Run ได้เลย" & vbCrLf &
        "-- ============================================" & vbCrLf &
        "UPDATE barcode_inspection.record_barcode_pass" & vbCrLf &
        "SET work_order = 'Correct Work Order'" & vbCrLf &
        "WHERE work_order = 'Wrong Work Order'" & vbCrLf &
        "AND product_name = 'Product Name';" & vbCrLf & vbCrLf &
        "-- ============================================" & vbCrLf &
        "-- Case 2: WO ผิด → WO ที่เคยมีอยู่แล้วใน database" & vbCrLf &
        "-- ต้องรันทีละ Step! คลุมทีละ Step แล้วกด Run" & vbCrLf &
        "-- ============================================" & vbCrLf &
        "-- Step 1: ดู MAX part_no ของ WO ที่ถูก (คลุมแล้วกด Run)" & vbCrLf &
        "SELECT MAX(part_no) FROM barcode_inspection.record_barcode_pass" & vbCrLf &
        "WHERE work_order = 'Correct Work Order';" & vbCrLf & vbCrLf &
        "-- Step 2: Renumber part_no (เปลี่ยน 999 เป็นผลจาก Step 1 แล้วคลุมกด Run)" & vbCrLf &
        "UPDATE barcode_inspection.record_barcode_pass" & vbCrLf &
        "SET part_no = part_no + 999" & vbCrLf &
        "WHERE work_order = 'Wrong Work Order'" & vbCrLf &
        "AND product_name = 'Product Name';" & vbCrLf & vbCrLf &
        "-- Step 3: ย้าย WO (คลุมแล้วกด Run)" & vbCrLf &
        "UPDATE barcode_inspection.record_barcode_pass" & vbCrLf &
        "SET work_order = 'Correct Work Order'" & vbCrLf &
        "WHERE work_order = 'Wrong Work Order'" & vbCrLf &
        "AND product_name = 'Product Name';"

        RichTextBox1.Clear()
        RichTextBox1.Rtf = Nothing
        RichTextBox1.Text = template

        HighlightWord(RichTextBox1, "Wrong Work Order", Color.Red)
        HighlightWord(RichTextBox1, "Correct Work Order", Color.Orange)
        HighlightWord(RichTextBox1, "Product Name", Color.DeepSkyBlue)
        HighlightWord(RichTextBox1, "999", Color.LimeGreen)
        HighlightWord(RichTextBox1, "Case 1:", Color.Blue)
        HighlightWord(RichTextBox1, "Case 2:", Color.Blue)
        HighlightWord(RichTextBox1, "Step 1:", Color.Silver)
        HighlightWord(RichTextBox1, "Step 2:", Color.Silver)
        HighlightWord(RichTextBox1, "Step 3:", Color.Silver)
    End Sub

    Private Sub HighlightWord(rtb As System.Windows.Forms.RichTextBox, word As String, color As Color)
        Dim startIndex As Integer = 0
        Do
            startIndex = rtb.Text.IndexOf(word, startIndex)
            If startIndex < 0 Then Exit Do
            rtb.Select(startIndex, word.Length)
            rtb.SelectionColor = color
            startIndex += word.Length
        Loop
    End Sub

    Private Sub Button27_Click(sender As Object, e As EventArgs) Handles Button27.Click
        Dim template As String =
            "SELECT table_name" & vbCrLf &
            "FROM information_schema.tables" & vbCrLf &
            "WHERE table_schema = 'barcode_inspection'" & vbCrLf &
            "ORDER BY table_name;"

        RichTextBox1.Clear()
        RichTextBox1.Text = template
    End Sub

    Private Sub Button28_Click(sender As Object, e As EventArgs) Handles Button28.Click
        Dim template As String =
            "UPDATE barcode_inspection.record_barcode_pass" & vbCrLf &
            "SET product_name = 'New Product Name'" & vbCrLf &
            "WHERE product_name = 'Old Product Name';"

        RichTextBox1.Clear()
        RichTextBox1.Text = template

        HighlightWord(RichTextBox1, "New Product Name", Color.Orange)
        HighlightWord(RichTextBox1, "Old Product Name", Color.Red)
    End Sub

#Region "Controllers for refactored tabs"

    ' Each controller runs one tab; Form1 only says which controls belong to it.
    ' Both stations share the same controller classes.
    Private dimensionInspection As StationInspectionController
    Private appearanceInspection As StationInspectionController
    Private dimensionReport As StationReportController
    Private appearanceReport As StationReportController
    Private finalReport As FinalReportController
    Private finalInspection As FinalInspectionController
    Private WithEvents settingsTab As SettingsController
    Private combinedCarton As CombinedCartonController
    Private reconciliation As ReconciliationController

    Private Sub CreateControllers()
        Dim products As New ProductSpecRepository(db)
        settingsTab = New SettingsController(SettingsTabView(), products)
        finalInspection = New FinalInspectionController(RecorderView(), New InspectionRepository(db), products)
        Dim dimensionData As New StationRepository(db, StationInfo.Dimension)
        Dim appearanceData As New StationRepository(db, StationInfo.Appearance)

        dimensionInspection = New StationInspectionController(StationInfo.Dimension, DimensionInspectionView(), dimensionData, products)
        appearanceInspection = New StationInspectionController(StationInfo.Appearance, AppearanceInspectionView(), appearanceData, products)
        dimensionReport = New StationReportController(StationInfo.Dimension, DimensionReportView(), dimensionData)
        appearanceReport = New StationReportController(StationInfo.Appearance, AppearanceReportView(), appearanceData)
        finalReport = New FinalReportController(FinalDataView(), New FinalReportRepository(db))
        combinedCarton = New CombinedCartonController(CombinedView(), New CombinedCartonRepository(db), products)
        reconciliation = New ReconciliationController(ReconciliationTabView(), New ReconciliationRepository(db), New InspectionRepository(db))
    End Sub

    Private Function DimensionInspectionView() As StationInspectionView
        Return New StationInspectionView With {
            .ProductCombo = ComboBox11, .WorkOrderCombo = ComboBox10, .AddWorkOrderButton = Button33,
            .ItemCodeLabel = Label61, .BarcodeFormatText = TextBox44, .HingeFormatText = TextBox42,
            .QuantityText = TextBox46, .RunningNoText = TextBox41, .LineNoText = TextBox45, .OperatorText = TextBox43,
            .StartButton = Button31, .BarcodeText = TextBox40, .HingeText = TextBox26, .InspectButton = Button32,
            .StatusLabel = Label40, .CountText = TextBox29, .Grid = DataGridView9,
            .DeleteMenuItem = DeleteSelectedDataToolStripMenuItem,
            .Modulus34Check = CheckBox37, .Modulus36Check = CheckBox36, .DimensionCheck = CheckBox35,
            .ElectricalCheck = CheckBox34, .AppearanceCheck = CheckBox33, .HingeCheck = CheckBox32,
            .CmosCheck = CheckBox31, .CompareCheck = CheckBox30, .MachineCheck = CheckBox29, .RunningNoCheck = CheckBox28,
            .ManualModeRadio = RadioButton1, .PassModeRadio = RadioButton2, .FailModeRadio = RadioButton3
        }
    End Function

    Private Function AppearanceInspectionView() As StationInspectionView
        Return New StationInspectionView With {
            .ProductCombo = ComboBox13, .WorkOrderCombo = ComboBox12, .AddWorkOrderButton = Button36,
            .ItemCodeLabel = Label77, .BarcodeFormatText = TextBox54, .HingeFormatText = TextBox52,
            .QuantityText = TextBox56, .RunningNoText = TextBox51, .LineNoText = TextBox55, .OperatorText = TextBox53,
            .StartButton = Button34, .BarcodeText = TextBox50, .HingeText = TextBox47, .InspectButton = Button35,
            .StatusLabel = Label71, .CountText = TextBox48, .Grid = DataGridView10,
            .DeleteMenuItem = DeleteSelectedDataToolStripMenuItem1,
            .Modulus34Check = CheckBox47, .Modulus36Check = CheckBox46, .DimensionCheck = CheckBox45,
            .ElectricalCheck = CheckBox44, .AppearanceCheck = CheckBox43, .HingeCheck = CheckBox42,
            .CmosCheck = CheckBox41, .CompareCheck = CheckBox40, .MachineCheck = CheckBox39, .RunningNoCheck = CheckBox38,
            .ManualModeRadio = RadioButton4, .PassModeRadio = RadioButton5, .FailModeRadio = RadioButton6
        }
    End Function

    Private Function DimensionReportView() As StationReportView
        Return New StationReportView With {
            .ProductCombo = ComboBox16, .WorkOrderFilter = CheckBox54, .WorkOrderCombo = ComboBox15,
            .DateFilter = CheckBox52, .DateFrom = DateTimePicker3, .DateTo = DateTimePicker4,
            .BarcodeFilter = CheckBox51, .BarcodeText = TextBox59, .HingeFilter = CheckBox50, .HingeText = TextBox58,
            .SearchButton = Button38, .ClearButton = Button37, .ExportButton = Button39,
            .PassGrid = DataGridView11, .PassCount = TextBox61, .PassClearMenuItem = ToolStripMenuItem10, .PassDeleteMenuItem = ToolStripMenuItem13,
            .FailGrid = DataGridView12, .FailCount = TextBox60, .FailClearMenuItem = ToolStripMenuItem14, .FailDeleteMenuItem = ToolStripMenuItem15
        }
    End Function

    Private Function AppearanceReportView() As StationReportView
        Return New StationReportView With {
            .ProductCombo = ComboBox17, .WorkOrderFilter = CheckBox57, .WorkOrderCombo = ComboBox14,
            .DateFilter = CheckBox55, .DateFrom = DateTimePicker5, .DateTo = DateTimePicker6,
            .BarcodeFilter = CheckBox49, .BarcodeText = TextBox64, .HingeFilter = CheckBox48, .HingeText = TextBox63,
            .SearchButton = Button41, .ClearButton = Button40, .ExportButton = Button42,
            .PassGrid = DataGridView13, .PassCount = TextBox66, .PassClearMenuItem = ToolStripMenuItem16, .PassDeleteMenuItem = ToolStripMenuItem17,
            .FailGrid = DataGridView14, .FailCount = TextBox65, .FailClearMenuItem = ToolStripMenuItem18, .FailDeleteMenuItem = ToolStripMenuItem19
        }
    End Function

    Private Function FinalDataView() As FinalReportView
        Return New FinalReportView With {
            .ProductCombo = ComboBox3, .WorkOrderFilter = CheckBox21, .WorkOrderCombo = ComboBox4,
            .CartonFilter = CheckBox22, .CartonText = TextBox21, .DateFilter = CheckBox23, .DateFrom = DateTimePicker1, .DateTo = DateTimePicker2,
            .BarcodeFilter = CheckBox24, .BarcodeText = TextBox22, .HingeFilter = CheckBox25, .HingeText = TextBox23,
            .DefectFilter = CheckBox26, .CombinedFilter = CheckBox27, .CombinedCombo = ComboBox6,
            .SearchButton = Button14, .ClearButton = Button15, .ExportButton = Button16,
            .PassGrid = DataGridView3, .PassCount = TextBox24, .PassClearSelectedMenuItem = ToolStripMenuItem7,
            .PassClearAllMenuItem = ToolStripMenuItem8, .PassDeleteMenuItem = ToolStripMenuItem9,
            .DeleteCombinedMenuItem = DeleteCombinedToolStripMenuItem,
            .FailGrid = DataGridView4, .FailCount = TextBox25, .FailClearAllMenuItem = ToolStripMenuItem11, .FailDeleteMenuItem = ToolStripMenuItem12
        }
    End Function

    ''' <summary>Every tab that has a product drop-down gets the new list when products change.</summary>
    Private Sub SettingsTab_ProductsLoaded(productNames As IReadOnlyList(Of String)) Handles settingsTab.ProductsLoaded
        For Each combo In {cboProduct, ComboBox3, ComboBox7, ComboBox9, ComboBox11, ComboBox13, ComboBox16, ComboBox17}
            combo.Items.Clear()
            combo.Items.AddRange(productNames.Cast(Of Object)().ToArray())
        Next
    End Sub

    Private Function SettingsTabView() As SettingsView
        Return New SettingsView With {
            .PasswordLabel = Label23, .PasswordText = TextBox20, .UnlockButton = Button13,
            .EltInputFolder = txtEltInputFolder, .EltOutputFolder = txtEltOutputFolder, .HingeFolder = txtHingeFolder,
            .AviStation1Folder = txtAviSt1Folder, .AviStation2Folder = txtAviSt2Folder,
            .EltInputBrowse = Button8, .EltOutputBrowse = Button9, .HingeBrowse = Button10, .AviStation1Browse = Button11, .AviStation2Browse = Button12,
            .ProductGrid = DataGridView2, .ProductNameText = TextBox10, .ItemCodeText = TextBox11, .BarcodeSpecText = TextBox12,
            .HingeSpecText = TextBox13, .QuantityText = TextBox14,
            .Modulus34Check = CheckBox11, .Modulus36Check = CheckBox12, .DimensionCheck = CheckBox13, .ElectricalCheck = CheckBox14,
            .AppearanceCheck = CheckBox15, .HingeCheck = CheckBox16, .CmosCheck = CheckBox17, .CompareCheck = CheckBox18,
            .MachineCheck = CheckBox19, .RunningCheck = CheckBox20,
            .NewButton = Button4, .EditButton = Button5, .DeleteButton = Button6, .CancelButton = Button7,
            .ChangePasswordButton = btnChangePassword
        }
    End Function

    Private Function CombinedView() As CombinedCartonView
        Return New CombinedCartonView With {
            .ProductCombo = ComboBox9, .ItemCodeLabel = Label33, .BarcodeFormatText = TextBox31, .CapacityText = TextBox33,
            .OperatorText = TextBox30, .CartonText = TextBox37, .LoadButton = Button29,
            .BarcodeText = TextBox39, .AddButton = Button30, .StatusLabel = Label41, .CountText = TextBox34, .Grid = DataGridView6
        }
    End Function

    Private Function RecorderView() As FinalInspectionView
        Return New FinalInspectionView With {
            .ProductCombo = cboProduct, .WorkOrderCombo = cboWorkOrder, .AddWorkOrderButton = btnAddWorkOrder, .ItemCodeLabel = Label2,
            .BarcodeFormatText = txtBarcodeFormat, .HingeFormatText = txtHingeFormat, .CapacityText = txtCartonCapacity,
            .RunningNoText = txtRunningNoRange, .LineNoText = txtLineNo, .OperatorText = txtOperatorId, .CartonText = txtCartonNo,
            .LoadButton = btnLoadCarton, .BarcodeText = txtBarcode, .HingeText = txtHinge, .InspectButton = btnInspect,
            .StatusLabel = lblInspectionStatus, .CountText = txtCartonCount, .Grid = dgvCartonRecords,
            .Checksum34Check = chkChecksum34, .Checksum36Check = chkChecksum36, .DimensionCheck = chkDimension,
            .ElectricalCheck = chkElectrical, .AppearanceCheck = chkAppearance, .HingeDataCheck = chkHingeData,
            .CmosCheck = chkRequireHinge, .CompareCheck = chkCompareBarcodeHinge, .AviCheck = chkAvi, .RunningNoCheck = chkRunningNo,
            .EltInputFolder = txtEltInputFolder, .EltOutputFolder = txtEltOutputFolder, .HingeFolder = txtHingeFolder,
            .AviStation1Folder = txtAviSt1Folder, .AviStation2Folder = txtAviSt2Folder
        }
    End Function

    Private Function ReconciliationTabView() As ReconciliationView
        Return New ReconciliationView With {
            .ProductCombo = ComboBox7, .BarcodeText = TextBox35, .AddButton = Button17,
            .ScannedGrid = DataGridView7, .ScannedCount = TextBox36,
            .ScannedClearSelectedMenuItem = ToolStripMenuItem1, .ScannedClearAllMenuItem = ToolStripMenuItem2,
            .WorkOrderCombo = ComboBox8, .CartonCombo = ComboBox5, .CompareButton = Button18,
            .MissingGrid = DataGridView8, .MissingCount = TextBox38,
            .MissingClearSelectedMenuItem = ToolStripMenuItem3, .MissingClearAllMenuItem = ToolStripMenuItem4,
            .MissingDeleteSelectedMenuItem = ToolStripMenuItem5, .MissingDeleteAllMenuItem = ToolStripMenuItem6,
            .ResetButton = Button19
        }
    End Function

#End Region

End Class
