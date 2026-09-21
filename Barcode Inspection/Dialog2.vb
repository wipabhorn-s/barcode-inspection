Imports System.Windows.Forms

Public Class Dialog2
    Public Property SelectedResult As Boolean = False
    Public Property Remark As String = ""

    Public Sub SetBarcode(barcode As String)
        Label2.Text = barcode
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        SelectedResult = True
        Remark = ""
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        Dim remarkInput As String = InputBox("Please enter reason for FAIL:", "Fail Reason")

        If String.IsNullOrWhiteSpace(remarkInput) Then
            MessageBox.Show("Please enter a reason.", "Required", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        SelectedResult = False
        Remark = remarkInput

        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub
End Class
