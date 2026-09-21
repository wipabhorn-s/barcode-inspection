Option Strict On

Imports System.IO

Namespace Services

    ''' <summary>Torque measurements from the hinge tester's CSV file.</summary>
    Public Class HingeData
        Public Property Max15 As Decimal?
        Public Property Avg15 As Decimal?
        Public Property Max120 As Decimal?
        Public Property Avg120 As Decimal?
        Public Property Angle As Decimal?
        Public Property Vendor As String
        Public Property DateCode As Date?

        ''' <summary>All four torque values are required for a part to pass.</summary>
        Public ReadOnly Property IsComplete As Boolean
            Get
                Return Max15.HasValue AndAlso Avg15.HasValue AndAlso Max120.HasValue AndAlso Avg120.HasValue
            End Get
        End Property
    End Class

    Public Class HingeReadResult
        Public Property Data As HingeData
        ''' <summary>Why the file could not be read; Nothing on success.</summary>
        Public Property ErrorMessage As String
    End Class

    ''' <summary>Reads &lt;hinge&gt;.csv: a header line followed by one line of values.</summary>
    Public Class HingeFileReader

        Public Function Read(hingeFolder As String, hinge As String) As HingeReadResult
            Dim csvPath As String = Path.Combine(hingeFolder, $"{hinge}.csv")

            If Not File.Exists(csvPath) Then Return New HingeReadResult With {.ErrorMessage = "Hinge file not found"}

            Try
                Using reader As New StreamReader(csvPath)
                    Dim headerLine = reader.ReadLine()
                    Dim valueLine = reader.ReadLine()

                    If headerLine Is Nothing OrElse valueLine Is Nothing Then
                        Return New HingeReadResult With {.ErrorMessage = "Hinge file format error"}
                    End If

                    Return New HingeReadResult With {.Data = Parse(headerLine, valueLine)}
                End Using
            Catch ex As Exception
                Return New HingeReadResult With {.ErrorMessage = $"Error reading hinge file: {ex.Message}"}
            End Try
        End Function

        ''' <summary>Maps tester column names to fields. Unknown columns are ignored.</summary>
        Public Shared Function Parse(headerLine As String, valueLine As String) As HingeData
            Dim headers = headerLine.Split(","c)
            Dim values = valueLine.Split(","c)
            Dim data As New HingeData()

            For i As Integer = 0 To headers.Length - 1
                Dim header As String = headers(i).Trim()
                Dim value As String = If(i < values.Length, values(i).Trim(), "")

                If header.Contains("Max(15-120)") Then
                    data.Max15 = ParseDecimalOrNull(value)
                ElseIf header.Contains("Avg(15-120)") Then
                    data.Avg15 = ParseDecimalOrNull(value)
                ElseIf header.Contains("Max(120-15)") Then
                    data.Max120 = ParseDecimalOrNull(value)
                ElseIf header.Contains("Avg(120-15)") Then
                    data.Avg120 = ParseDecimalOrNull(value)
                ElseIf header.Contains("Angle") Then
                    data.Angle = ParseDecimalOrNull(value)
                ElseIf header.Contains("Clutch Vendor") Then
                    data.Vendor = If(String.IsNullOrWhiteSpace(value), Nothing, value)
                ElseIf header.Contains("Clutch DC") Then
                    data.DateCode = ParseDateOrNull(value)
                End If
            Next

            Return data
        End Function

        Private Shared Function ParseDecimalOrNull(value As String) As Decimal?
            Dim result As Decimal
            If Decimal.TryParse(value, result) Then Return result
            Return Nothing
        End Function

        Private Shared Function ParseDateOrNull(value As String) As Date?
            Dim result As Date
            If Date.TryParse(value, result) Then Return result
            Return Nothing
        End Function

    End Class

End Namespace
