Option Strict On

Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain

Namespace Services

    Public Class InspectionRequest
        Public Property ProductName As String
        Public Property WorkOrder As String
        Public Property Barcode As String
        Public Property Hinge As String
        Public Property CartonNo As Integer
        Public Property LineNo As Integer
        Public Property OperatorId As String
    End Class

    ''' <summary>What the operator chose when a barcode's running number is outside the allowed range.</summary>
    Public Enum RunningNoDecision
        None
        Rework
        Reject
    End Enum

    Public Class InspectionOutcome
        Public ReadOnly Property Inspection As InspectionData
        Public ReadOnly Property Failures As New List(Of InspectionFailure)
        ''' <summary>Set when the electrical check ran; its files are moved after a pass is saved.</summary>
        Public Property Electrical As ElectricalCheckResult

        Public Sub New(inspection As InspectionData)
            Me.Inspection = inspection
        End Sub

        Public Sub AddFailure(subject As String, message As String, priority As FailurePriority)
            Failures.Add(New InspectionFailure(subject, message, priority))
        End Sub

        ''' <summary>Fails the part with <paramref name="remark"/> (saved to the database) and tells the operator why.</summary>
        Public Sub Reject(remark As String, subject As String, message As String, priority As FailurePriority)
            Inspection.MarkFailed(remark)
            AddFailure(subject, message, priority)
        End Sub
    End Class

    ''' <summary>
    ''' Runs every enabled check on a scanned barcode, in order, and stops at the first failure.
    ''' It knows nothing about forms or controls: the UI passes in the values and shows the outcome.
    ''' </summary>
    Public Class BarcodeInspectionService

        Private ReadOnly _repository As IInspectionRepository
        Private ReadOnly _electricalReader As ElectricalResultReader
        Private ReadOnly _hingeReader As HingeFileReader
        Private ReadOnly _aviReader As AviResultReader

        Public Sub New(repository As IInspectionRepository,
                       Optional electricalReader As ElectricalResultReader = Nothing,
                       Optional hingeReader As HingeFileReader = Nothing,
                       Optional aviReader As AviResultReader = Nothing)
            _repository = repository
            _electricalReader = If(electricalReader, New ElectricalResultReader())
            _hingeReader = If(hingeReader, New HingeFileReader())
            _aviReader = If(aviReader, New AviResultReader())
        End Sub

        ''' <param name="askRunningNoDecision">
        ''' Called with (range, barcode) when the running number is out of range, so the UI can ask the operator.
        ''' </param>
        Public Async Function InspectAsync(request As InspectionRequest,
                                           options As InspectionOptions,
                                           askRunningNoDecision As Func(Of String, String, RunningNoDecision)) As Task(Of InspectionOutcome)
            Dim outcome As New InspectionOutcome(New InspectionData With {
                .ProductName = request.ProductName,
                .WorkOrder = request.WorkOrder,
                .Barcode = request.Barcode,
                .Hinge = If(String.IsNullOrEmpty(request.Hinge), Nothing, request.Hinge),
                .CartonNo = request.CartonNo,
                .LineNo = request.LineNo,
                .OperatorID = request.OperatorId
            })

            Await RunChecksAsync(outcome, options, askRunningNoDecision)
            Return outcome
        End Function

        ''' <summary>Saves the result, then moves the electrical files of a passed part to the output folder.</summary>
        Public Async Function SaveAsync(outcome As InspectionOutcome, options As InspectionOptions) As Task
            Await _repository.SaveAsync(outcome.Inspection)

            Dim electrical = outcome.Electrical
            If outcome.Inspection.IsPass AndAlso options.CheckElectrical AndAlso electrical IsNot Nothing AndAlso electrical.IsPass Then
                Await _electricalReader.MoveToOutputAsync(electrical, options.ElectricalOutputFolder)
            End If
        End Function

        ' Each check returns as soon as it rejects the part, so later checks never run for a failed part.
        Private Async Function RunChecksAsync(outcome As InspectionOutcome,
                                              options As InspectionOptions,
                                              askRunningNoDecision As Func(Of String, String, RunningNoDecision)) As Task
            Dim item = outcome.Inspection
            Dim barcode = item.Barcode
            Dim hinge = item.Hinge
            Dim hasHinge = Not String.IsNullOrEmpty(hinge)

            Dim rule = BarcodeRules.CheckBarcodeFormat(barcode, options.BarcodeTemplate)
            If Not rule.IsValid Then
                outcome.Reject("Barcode format mismatch", barcode, rule.Message, FailurePriority.BarcodeFormat)
                Return
            End If

            If hasHinge Then
                rule = BarcodeRules.CheckHingeFormat(hinge, options.HingeTemplate)
                If Not rule.IsValid Then
                    outcome.Reject("Hinge Or CMOS format mismatch", hinge, rule.Message, FailurePriority.HingeFormat)
                    Return
                End If
            End If

            Dim duplicateMessage = Await FindDuplicateAsync(barcode)
            If duplicateMessage IsNot Nothing Then
                outcome.Reject("Duplicate barcode", barcode, duplicateMessage, FailurePriority.Critical)
                Return
            End If

            If options.CheckChecksum34 Then
                rule = BarcodeRules.CheckChecksum34(barcode)
                If Not rule.IsValid Then
                    outcome.Reject("Checksum modulo-34 failed", barcode, rule.Message, FailurePriority.Checksum34)
                    Return
                End If
            End If

            If options.CheckChecksum36 Then
                rule = BarcodeRules.CheckChecksum36(barcode)
                If Not rule.IsValid Then
                    outcome.Reject("Checksum modulo-36 failed", barcode, rule.Message, FailurePriority.Checksum36)
                    Return
                End If
            End If

            If options.CheckDimension Then
                item.DimensionResult = Await _repository.DimensionRecordExistsAsync(barcode)
                If Not item.DimensionResult.Value Then
                    outcome.Reject("Dimension result Not found", barcode, "Dimension result not found", FailurePriority.Dimension)
                    Return
                End If
            End If

            If options.CheckElectrical Then
                outcome.Electrical = Await _electricalReader.CheckAsync(options.ElectricalInputFolder, barcode)
                item.ElectricalResult = outcome.Electrical.IsPass
                If Not outcome.Electrical.IsPass Then
                    outcome.Reject("Electrical test failed", barcode, outcome.Electrical.FailureMessage, FailurePriority.Electrical)
                    Return
                End If
            End If

            If options.CheckAppearance Then
                item.AppearanceResult = Await _repository.AppearanceRecordExistsAsync(barcode)
                If Not item.AppearanceResult.Value Then
                    outcome.Reject("Appearance result Not found", barcode, "Appearance result not found", FailurePriority.Appearance)
                    Return
                End If
            End If

            If options.CheckHingeData AndAlso hasHinge Then
                Dim hingeFile = _hingeReader.Read(options.HingeFolder, hinge)
                If hingeFile.Data Is Nothing Then
                    outcome.Reject("Hinge file Not found", hinge, hingeFile.ErrorMessage, FailurePriority.HingeData)
                    Return
                End If

                CopyHingeData(hingeFile.Data, item)
                If Not hingeFile.Data.IsComplete Then
                    outcome.Reject("Incomplete hinge data", hinge, "Incomplete hinge data", FailurePriority.HingeData)
                    Return
                End If
            End If

            If options.CheckBarcodeMatchesHinge AndAlso hasHinge AndAlso barcode.Trim() <> hinge.Trim() Then
                outcome.Reject("Barcode vs Hinge mismatch", $"{barcode} vs {hinge}", "Barcode mismatch", FailurePriority.Critical)
                Return
            End If

            If options.CheckAvi Then
                Dim avi = Await _aviReader.CheckAsync(barcode, options.AviStation1Folder, options.AviStation2Folder)
                item.MC1Result = avi.Station1Pass
                item.MC2Result = avi.Station2Pass
                For Each message In avi.FailureMessages
                    outcome.AddFailure(barcode, message, FailurePriority.Avi)
                Next

                If Not avi.Station1Pass OrElse Not avi.Station2Pass Then
                    item.MarkFailed(DescribeAviFailure(avi.Station1Pass, avi.Station2Pass))
                    Return
                End If
            End If

            If options.CheckRunningNo Then
                Dim rangeFrom, rangeTo, digitCount As Integer
                If BarcodeRules.TryParseRunningNoRange(options.RunningNoRange, rangeFrom, rangeTo, digitCount) AndAlso
                   Not BarcodeRules.IsRunningNoInRange(barcode, rangeFrom, rangeTo, digitCount) Then

                    Select Case askRunningNoDecision(options.RunningNoRange.Trim(), barcode)
                        Case RunningNoDecision.Rework
                            item.Remark = "Rework/Scrap" ' still a pass, but flagged
                        Case RunningNoDecision.Reject
                            item.MarkFailed("Rejected running no.")
                    End Select
                End If
            End If
        End Function

        ''' <summary>Returns the operator message if the barcode (or its base, before '+') has already passed.</summary>
        Private Async Function FindDuplicateAsync(barcode As String) As Task(Of String)
            If Await _repository.PassRecordExistsAsync(barcode) Then Return "Duplicate barcode detected"

            Dim plusIndex = barcode.IndexOf("+"c)
            If plusIndex >= 0 AndAlso Await _repository.PassRecordWithPrefixExistsAsync(barcode.Substring(0, plusIndex)) Then
                Return "Duplicate base barcode detected"
            End If

            Return Nothing
        End Function

        Private Shared Sub CopyHingeData(source As HingeData, target As InspectionData)
            target.HingeMax15 = source.Max15
            target.HingeAvg15 = source.Avg15
            target.HingeMax120 = source.Max120
            target.HingeAvg120 = source.Avg120
            target.HingeAngle = source.Angle
            target.HingeVendor = source.Vendor
            target.HingeDate = source.DateCode
        End Sub

        ''' <summary>
        ''' Checks that every folder an enabled check reads from is set and exists.
        ''' Returns the message and caption to show, or Nothing when all folders are fine.
        ''' </summary>
        Public Shared Function FindFolderProblem(options As InspectionOptions) As (Message As String, Caption As String)?
            Dim required As New List(Of (Name As String, Path As String))
            If options.CheckElectrical Then
                required.Add(("ELT input folder", options.ElectricalInputFolder))
                required.Add(("ELT output folder", options.ElectricalOutputFolder))
            End If
            If options.CheckHingeData Then required.Add(("Hinge folder", options.HingeFolder))
            If options.CheckAvi Then
                required.Add(("Station 1 AVI folder", options.AviStation1Folder))
                required.Add(("Station 2 AVI folder", options.AviStation2Folder))
            End If

            For Each folder In required
                If String.IsNullOrWhiteSpace(folder.Path) Then Return ($"{folder.Name} is missing. Please check the settings.", "Path Missing")
                If Not IO.Directory.Exists(folder.Path) Then Return ($"{folder.Name} does not exist. Please verify the folder path.", "Invalid Path")
            Next
            Return Nothing
        End Function

        Public Shared Function DescribeAviFailure(station1Pass As Boolean, station2Pass As Boolean) As String
            If Not station1Pass AndAlso Not station2Pass Then Return "AVI ST1 and ST2 failed"
            If Not station1Pass Then Return "AVI ST1 failed"
            If Not station2Pass Then Return "AVI ST2 failed"
            Return Nothing
        End Function

    End Class

End Namespace
