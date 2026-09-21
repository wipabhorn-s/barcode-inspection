Imports System.Windows.Forms

Public Class Password_Request
    Private Async Sub OK_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles OK_Button.Click
        OK_Button.Enabled = False
        Dim correct = Await UI.UiKit.CheckAdminPasswordAsync(Textbox1.Text)
        OK_Button.Enabled = True

        If Not correct Then
            Textbox1.Clear()
            Textbox1.Focus()
            Return
        End If

        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub


    Private Sub Cancel_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Cancel_Button.Click
        Me.DialogResult = System.Windows.Forms.DialogResult.Cancel
        Me.Close()
    End Sub

    Private Sub Password_Request_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Textbox1.Clear()
        Textbox1.Focus()
    End Sub
End Class
