Imports System.Windows.Forms

Public Class WOForm
    Public ReadOnly Property WorkOrder As String
        Get
            Return TextBox1.Text
        End Get
    End Property

    Private Sub WO_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Textbox1.Clear()
        Textbox1.Focus()
    End Sub

    Private Sub OK_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles OK_Button.Click
        If String.IsNullOrEmpty(TextBox1.Text) Then
            MessageBox.Show("Please enter a valid work order.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Textbox1.Focus()
        End If

        Me.DialogResult = System.Windows.Forms.DialogResult.OK
        Me.Close()
    End Sub

    Private Sub Cancel_Button_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Cancel_Button.Click
        Me.DialogResult = System.Windows.Forms.DialogResult.Cancel
        Me.Close()
    End Sub
End Class

