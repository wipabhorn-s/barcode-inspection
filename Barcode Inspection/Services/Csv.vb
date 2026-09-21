Option Strict On

Namespace Services

    Public NotInheritable Class Csv

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Quotes a value when it contains a comma, quote or line break (RFC 4180), doubling any quotes inside.
        ''' </summary>
        Public Shared Function Escape(value As String) As String
            If value Is Nothing Then Return ""

            If value.IndexOfAny({","c, """"c, ControlChars.Cr, ControlChars.Lf}) < 0 Then Return value
            Return """" & value.Replace("""", """""") & """"
        End Function

        ''' <summary>Formats a grid cell value: blank for NULL, dates as yyyy-MM-dd.</summary>
        Public Shared Function FormatCell(value As Object) As String
            If value Is Nothing OrElse TypeOf value Is DBNull Then Return ""
            If TypeOf value Is Date Then Return Escape(DirectCast(value, Date).ToString("yyyy-MM-dd"))
            Return Escape(value.ToString())
        End Function

    End Class

End Namespace
