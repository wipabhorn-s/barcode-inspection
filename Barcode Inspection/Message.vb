Imports System.Windows.Forms

Public Class Message

    Private Shared errorList As New List(Of Tuple(Of String, Integer))()
    Private Shared errorBarcode As String = ""
    Private Shared hasErrorShown As Boolean = False

    Public Shared Sub ResultFailCase(barcode As String, message As String, priority As Integer)
        If String.IsNullOrEmpty(errorBarcode) Then
            errorBarcode = barcode
        End If

        If Not errorList.Any(Function(e) e.Item1 = message) Then
            errorList.Add(Tuple.Create(message, priority))
        End If
    End Sub

    Public Shared Sub ShowFirstError()
        If errorList.Count = 0 Then Exit Sub

        Dim topError = errorList.OrderBy(Function(e) e.Item2).First()

        Dim dialog As New Message()
        dialog.Label1.Text = topError.Item1
        dialog.Label2.Text = errorBarcode
        dialog.ShowDialog()

        ResetErrorFlag()
    End Sub

    Public Shared Sub ResetErrorFlag()
        hasErrorShown = False
        errorBarcode = ""
        errorList.Clear()
    End Sub

End Class
