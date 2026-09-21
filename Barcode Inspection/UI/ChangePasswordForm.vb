Option Strict On

Imports System.Windows.Forms

Namespace UI

    ''' <summary>
    ''' Asks for the current password and the new one twice. <c>submit</c> saves the change and returns why it
    ''' was refused (the dialog stays open), or Nothing on success (the dialog closes).
    ''' </summary>
    Public Class ChangePasswordForm
        Inherits Form

        Private ReadOnly _submit As Func(Of String, String, String, Task(Of String))
        Private ReadOnly _current As New TextBox With {.UseSystemPasswordChar = True, .Width = 220}
        Private ReadOnly _new As New TextBox With {.UseSystemPasswordChar = True, .Width = 220}
        Private ReadOnly _confirm As New TextBox With {.UseSystemPasswordChar = True, .Width = 220}
        Private ReadOnly _save As New Button With {.Text = "Save", .Width = 80}
        Private ReadOnly _cancel As New Button With {.Text = "Cancel", .Width = 80, .DialogResult = DialogResult.Cancel}

        Public Sub New(submit As Func(Of String, String, String, Task(Of String)))
            _submit = submit

            Text = "Change Admin Password"
            FormBorderStyle = FormBorderStyle.FixedDialog
            StartPosition = FormStartPosition.CenterParent
            MaximizeBox = False
            MinimizeBox = False
            ShowInTaskbar = False
            AutoSize = True
            AutoSizeMode = AutoSizeMode.GrowAndShrink
            AcceptButton = _save
            CancelButton = _cancel

            Dim layout As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .Padding = New Padding(12)}
            AddRow(layout, "Current password", _current)
            AddRow(layout, "New password", _new)
            AddRow(layout, "Confirm new password", _confirm)

            Dim buttons As New FlowLayoutPanel With {.FlowDirection = FlowDirection.RightToLeft, .AutoSize = True, .Dock = DockStyle.Fill}
            buttons.Controls.Add(_cancel)
            buttons.Controls.Add(_save)
            layout.Controls.Add(buttons)
            layout.SetColumnSpan(buttons, 2)
            Controls.Add(layout)

            AddHandler _save.Click, AddressOf Save
        End Sub

        Private Shared Sub AddRow(layout As TableLayoutPanel, caption As String, box As TextBox)
            layout.Controls.Add(New Label With {.Text = caption, .AutoSize = True, .Anchor = AnchorStyles.Left, .Margin = New Padding(3, 6, 12, 3)})
            layout.Controls.Add(box)
        End Sub

        Private Async Sub Save(sender As Object, e As EventArgs)
            _save.Enabled = False
            Try
                Dim problem = Await _submit(_current.Text, _new.Text, _confirm.Text)
                If problem Is Nothing Then
                    DialogResult = DialogResult.OK
                    Close()
                    Return
                End If
                UiKit.Warning(problem, "Change Password")
                _new.Clear()
                _confirm.Clear()
                _current.Focus()
            Finally
                _save.Enabled = True
            End Try
        End Sub

    End Class

End Namespace
