Option Strict On

Imports System.IO

Namespace Services

    Public Class AviCheckResult
        Public Property Station1Pass As Boolean
        Public Property Station2Pass As Boolean
        Public ReadOnly Property FailureMessages As New List(Of String)
    End Class

    ''' <summary>
    ''' Reads the automated visual inspection (AVI) results. Each of the two stations writes
    ''' &lt;barcode&gt;_*.csv, and the latest file's name contains PASS when the part passed.
    ''' </summary>
    Public Class AviResultReader

        Private ReadOnly _timeout As TimeSpan

        Public Sub New(Optional timeout As TimeSpan? = Nothing)
            _timeout = If(timeout, TimeSpan.FromSeconds(5))
        End Sub

        Public Async Function CheckAsync(barcode As String, station1Folder As String, station2Folder As String) As Task(Of AviCheckResult)
            Dim station1 = Task.Run(Function() CheckStation("ST1", station1Folder, barcode))
            Dim station2 = Task.Run(Function() CheckStation("ST2", station2Folder, barcode))
            Dim both = Task.WhenAll(station1, station2)

            Dim result As New AviCheckResult()
            Dim finishedInTime = (Await Task.WhenAny(both, Task.Delay(_timeout))) Is both

            For Each station In {station1, station2}
                If station.Status = TaskStatus.RanToCompletion AndAlso station.Result.Message IsNot Nothing Then
                    result.FailureMessages.Add(station.Result.Message)
                End If
            Next

            If finishedInTime Then
                result.Station1Pass = station1.Result.Passed
                result.Station2Pass = station2.Result.Passed
            Else
                Debug.WriteLine("AVI check timeout")
                result.FailureMessages.Add("AVI check timeout")
            End If

            Return result
        End Function

        Private Shared Function CheckStation(station As String, folder As String, barcode As String) As (Passed As Boolean, Message As String)
            Try
                Dim latestFile = New DirectoryInfo(folder).
                    EnumerateFiles($"{barcode}_*.csv", SearchOption.TopDirectoryOnly).
                    OrderByDescending(Function(f) f.LastWriteTime).
                    FirstOrDefault()

                If latestFile Is Nothing Then Return (False, $"AVI {station} file missing")

                Dim passed = latestFile.Name.IndexOf("PASS", StringComparison.OrdinalIgnoreCase) >= 0
                Return (passed, If(passed, Nothing, $"AVI {station} failed"))
            Catch ex As Exception
                Debug.WriteLine($"AVI {station} Error: {ex.Message}")
                Return (False, $"AVI {station} error")
            End Try
        End Function

    End Class

End Namespace
