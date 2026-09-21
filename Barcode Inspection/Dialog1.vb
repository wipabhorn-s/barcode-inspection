Public Class Dialog1
    Public Property SelectedAction As String = Nothing

    Public Sub SetInfo(rangeText As String, barcode As String)
        Label1.Text = $"Running No. out of range: {rangeText}"
        Label2.Text = $"Barcode: {barcode}"
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If e.CloseReason = CloseReason.UserClosing AndAlso SelectedAction Is Nothing Then
            e.Cancel = True
        End If

        MyBase.OnFormClosing(e)
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        SelectedAction = "Rework"
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub

    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        SelectedAction = "Reject"
        Me.DialogResult = DialogResult.OK
        Me.Close()
    End Sub
End Class