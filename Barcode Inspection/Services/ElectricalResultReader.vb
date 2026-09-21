Option Strict On

Imports System.IO

Namespace Services

    Public Class ElectricalCheckResult
        Public Property IsPass As Boolean
        Public Property TxtFilePath As String
        Public Property CsvFilePath As String
        ''' <summary>Why the check failed; Nothing when it passed.</summary>
        Public Property FailureMessage As String
    End Class

    ''' <summary>
    ''' Reads the electrical tester's output. The tester writes either a .txt file whose name contains
    ''' PASS/FAIL, or a .csv file with PASS/FAIL in its first lines.
    ''' </summary>
    Public Class ElectricalResultReader

        Private Const CsvLinesToScan As Integer = 20

        Public Function CheckAsync(inputFolder As String, barcode As String) As Task(Of ElectricalCheckResult)
            Return Task.Run(Function() Check(inputFolder, barcode))
        End Function

        Private Function Check(inputFolder As String, barcode As String) As ElectricalCheckResult
            Try
                Dim txtFile = FindLatestFile(inputFolder, $"*{barcode}*.txt")
                If txtFile IsNot Nothing Then
                    Dim verdict = ClassifyFileName(txtFile.Name)
                    If Not verdict.HasValue Then Return Failed("Electrical result unclear")
                    Return If(verdict.Value, Passed(txtPath:=txtFile.FullName), Failed("Electrical test failed"))
                End If

                Dim csvFile = FindLatestFile(inputFolder, $"*{barcode}*.csv")
                If csvFile IsNot Nothing Then
                    Dim verdict = ClassifyCsv(File.ReadLines(csvFile.FullName).Take(CsvLinesToScan))
                    If Not verdict.HasValue Then Return Failed("Electrical result not found in CSV")
                    Return If(verdict.Value, Passed(csvPath:=csvFile.FullName), Failed("Electrical test failed (CSV)"))
                End If

                Return Failed("Electrical file not found")
            Catch ex As Exception
                Return Failed($"Error checking electrical: {ex.Message}")
            End Try
        End Function

        ''' <summary>True if the name says PASS, False if it says FAIL, Nothing if it says neither.</summary>
        Public Shared Function ClassifyFileName(fileName As String) As Boolean?
            If ContainsWord(fileName, "PASS") Then Return True
            If ContainsWord(fileName, "FAIL") Then Return False
            Return Nothing
        End Function

        ''' <summary>A CSV passes only if it mentions PASS and never FAIL.</summary>
        Public Shared Function ClassifyCsv(lines As IEnumerable(Of String)) As Boolean?
            Dim hasPass = False
            Dim hasFail = False

            For Each line In lines
                hasPass = hasPass OrElse ContainsWord(line, "PASS")
                hasFail = hasFail OrElse ContainsWord(line, "FAIL")
                If hasPass AndAlso hasFail Then Exit For
            Next

            If hasFail Then Return False
            If hasPass Then Return True
            Return Nothing
        End Function

        ''' <summary>Moves the result files of a passed part to the output folder, replacing older copies.</summary>
        Public Function MoveToOutputAsync(result As ElectricalCheckResult, outputFolder As String) As Task
            Return Task.Run(Sub()
                                Try
                                    If Not Directory.Exists(outputFolder) Then
                                        Debug.WriteLine($"Output folder not found: {outputFolder}")
                                        Return
                                    End If

                                    MoveReplacing(result.TxtFilePath, outputFolder)
                                    MoveReplacing(result.CsvFilePath, outputFolder)
                                Catch ex As Exception
                                    Debug.WriteLine($"MoveElectricalFiles Error: {ex.Message}")
                                End Try
                            End Sub)
        End Function

        Private Shared Sub MoveReplacing(sourcePath As String, outputFolder As String)
            If String.IsNullOrEmpty(sourcePath) OrElse Not File.Exists(sourcePath) Then Return

            Dim destinationPath = Path.Combine(outputFolder, Path.GetFileName(sourcePath))
            If File.Exists(destinationPath) Then File.Delete(destinationPath)
            File.Move(sourcePath, destinationPath)
        End Sub

        Private Shared Function FindLatestFile(folder As String, pattern As String) As FileInfo
            Return New DirectoryInfo(folder).
                EnumerateFiles(pattern, SearchOption.TopDirectoryOnly).
                OrderByDescending(Function(f) f.LastWriteTime).
                FirstOrDefault()
        End Function

        Private Shared Function ContainsWord(text As String, word As String) As Boolean
            Return text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
        End Function

        Private Shared Function Passed(Optional txtPath As String = Nothing, Optional csvPath As String = Nothing) As ElectricalCheckResult
            Return New ElectricalCheckResult With {.IsPass = True, .TxtFilePath = txtPath, .CsvFilePath = csvPath}
        End Function

        Private Shared Function Failed(message As String) As ElectricalCheckResult
            Return New ElectricalCheckResult With {.IsPass = False, .FailureMessage = message}
        End Function

    End Class

End Namespace
