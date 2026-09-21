Option Strict On

Imports System.IO
Imports System.Text
Imports System.Windows.Forms
Imports Barcode_Inspection.Services
Imports Npgsql

Namespace UI

    ''' <summary>Message boxes and confirmations shared by the controllers.</summary>
    Friend NotInheritable Class UiKit

        Private Sub New()
        End Sub

        ''' <summary>Every message box goes through here. Tests replace it so dialogs do not block them.</summary>
        Friend Shared ShowMessage As Func(Of String, String, MessageBoxButtons, MessageBoxIcon, MessageBoxDefaultButton, DialogResult) =
            Function(text, caption, buttons, icon, defaultButton) MessageBox.Show(text, caption, buttons, icon, defaultButton)

        Public Shared Sub Info(text As String, Optional caption As String = "Information")
            ShowMessage(text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1)
        End Sub

        Public Shared Sub Warning(text As String, Optional caption As String = "Input Required")
            ShowMessage(text, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1)
        End Sub

        Public Shared Sub [Error](text As String, Optional caption As String = "Error")
            ShowMessage(text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1)
        End Sub

        ''' <summary>Yes/No question with "No" as the default button, so Enter does not confirm by accident.</summary>
        Public Shared Function Confirm(question As String, caption As String) As Boolean
            Return ShowMessage(question, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) = DialogResult.Yes
        End Function

        ''' <summary>Shows a warning, puts the cursor in <paramref name="focus"/>, and returns False for use in validation.</summary>
        Public Shared Function Invalid(text As String, Optional caption As String = "Input Required", Optional focus As TextBox = Nothing) As Boolean
            Warning(text, caption)
            If focus IsNot Nothing Then
                focus.Focus()
                focus.SelectAll()
            End If
            Return False
        End Function

        ''' <summary>
        ''' Checks the admin password against the hash in the database. Shows a message and returns False when it is
        ''' wrong, not set up yet, or the database cannot be reached.
        ''' </summary>
        Public Shared Async Function CheckAdminPasswordAsync(password As String) As Task(Of Boolean)
            Try
                Select Case Await AdminPasswordService.Current.CheckAsync(password)
                    Case PasswordCheck.Correct
                        Return True
                    Case PasswordCheck.Incorrect
                        Warning("Incorrect password. Please try again.", "Access Denied")
                    Case Else
                        [Error](AdminPasswordService.NotConfiguredMessage, "Admin Password")
                End Select
            Catch ex As PostgresException When ex.SqlState = PostgresErrorCodes.UndefinedTable
                [Error](AdminPasswordService.NotConfiguredMessage, "Admin Password")
            Catch ex As Exception
                [Error]($"Cannot check the password: {ex.Message}", "Connection Error")
            End Try
            Return False
        End Function

        ''' <summary>Shows the admin password dialog; True when the right password was entered. Tests replace it.</summary>
        Friend Shared AskAdminPassword As Func(Of Boolean) =
            Function()
                Using frm As New Password_Request()
                    Return frm.ShowDialog() = DialogResult.OK
                End Using
            End Function

        ''' <summary>Asks for the admin password, then for confirmation.</summary>
        Public Shared Function ConfirmAdminDelete(question As String) As Boolean
            If Not AskAdminPassword() Then Return False
            Return ShowMessage(question, "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) = DialogResult.Yes
        End Function

        ''' <summary>Runs <paramref name="action"/> when Enter is pressed and stops the beep.</summary>
        Public Shared Sub OnEnter(e As KeyEventArgs, action As Action)
            If e.KeyCode <> Keys.Enter Then Return
            e.SuppressKeyPress = True
            action()
        End Sub

        ''' <summary>
        ''' Keeps only the last <paramref name="maxLength"/> characters, so a second badge scan replaces the first
        ''' instead of being appended to it.
        ''' </summary>
        Public Shared Sub KeepLastCharacters(box As TextBox, maxLength As Integer)
            AddHandler box.TextChanged,
                Sub()
                    If box.TextLength <= maxLength Then Return
                    box.Text = box.Text.Substring(box.TextLength - maxLength)
                    box.SelectionStart = box.TextLength
                End Sub
        End Sub

        ''' <summary>A filter check box enables its combo box; unticking it clears the selection.</summary>
        Public Shared Sub BindFilter(filter As CheckBox, combo As ComboBox)
            AddHandler filter.CheckedChanged,
                Sub()
                    combo.Enabled = filter.Checked
                    If filter.Checked Then Return
                    combo.SelectedIndex = -1
                    combo.Text = ""
                End Sub
        End Sub

        ''' <summary>A filter check box enables its text box and moves the cursor there; unticking it clears the text.</summary>
        Public Shared Sub BindFilter(filter As CheckBox, box As TextBox)
            AddHandler filter.CheckedChanged,
                Sub()
                    box.Enabled = filter.Checked
                    If filter.Checked Then
                        box.Focus()
                        box.SelectAll()
                    Else
                        box.Clear()
                    End If
                End Sub
        End Sub

        ''' <summary>A filter check box enables a date range; unticking it resets both dates to today.</summary>
        Public Shared Sub BindFilter(filter As CheckBox, dateFrom As DateTimePicker, dateTo As DateTimePicker)
            AddHandler filter.CheckedChanged,
                Sub()
                    If Not filter.Checked Then
                        dateFrom.Value = Date.Today
                        dateTo.Value = Date.Today
                    End If
                    dateFrom.Enabled = filter.Checked
                    dateTo.Enabled = filter.Checked
                End Sub
        End Sub

        ''' <summary>Asks for a file name and writes the grids' visible columns as CSV, one grid after another.</summary>
        Public Shared Sub ExportToCsv(defaultName As String, title As String, headerGrid As DataGridView, ParamArray grids As DataGridView())
            Using dialog As New SaveFileDialog()
                dialog.Filter = "CSV file (*.csv)|*.csv"
                dialog.FileName = $"{defaultName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                dialog.Title = title
                If dialog.ShowDialog() <> DialogResult.OK Then Return

                Try
                    Using writer As New StreamWriter(dialog.FileName, False, Encoding.UTF8)
                        writer.WriteLine(String.Join(",", VisibleColumns(headerGrid).Select(Function(c) Csv.Escape(c.HeaderText))))
                        For Each grid In grids
                            Dim columns = VisibleColumns(grid)
                            For Each row As DataGridViewRow In grid.Rows
                                If row.IsNewRow Then Continue For
                                writer.WriteLine(String.Join(",", columns.Select(Function(c) Csv.FormatCell(row.Cells(c.Index).Value))))
                            Next
                        Next
                    End Using
                    Info("Export completed successfully.", "Success")
                Catch ex As Exception
                    [Error]($"Export failed: {ex.Message}")
                End Try
            End Using
        End Sub

        Private Shared Function VisibleColumns(grid As DataGridView) As List(Of DataGridViewColumn)
            Return grid.Columns.Cast(Of DataGridViewColumn)().Where(Function(c) c.Visible).ToList()
        End Function

    End Class

End Namespace
