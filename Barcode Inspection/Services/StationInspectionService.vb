Option Strict On

Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain

Namespace Services

    ''' <summary>The operator's PASS/FAIL decision in manual judgement mode.</summary>
    Public Class StationJudgement
        Public Property IsPass As Boolean
        Public Property Remark As String
    End Class

    ''' <summary>Questions the service needs the operator to answer. The form implements these with dialogs.</summary>
    Public Interface IStationPrompts
        Function AskRunningNoDecision(rangeText As String, barcode As String) As RunningNoDecision
        ''' <summary>Returns the fail reason, or Nothing when the operator cancels the scan.</summary>
        Function AskFailReason() As String
        ''' <summary>Returns the operator's decision, or Nothing when the operator cancels the scan.</summary>
        Function AskJudgement(barcode As String) As StationJudgement
    End Interface

    Public Enum StationScanStatus
        ''' <summary>A barcode check failed. Nothing is saved.</summary>
        Rejected
        ''' <summary>The operator cancelled. Nothing is saved.</summary>
        Cancelled
        ''' <summary>A PASS or FAIL result is ready to be saved.</summary>
        Judged
    End Enum

    Public Class StationInspectionOutcome
        Inherits InspectionOutcome

        Public Property Status As StationScanStatus = StationScanStatus.Judged

        Public Sub New(inspection As InspectionData)
            MyBase.New(inspection)
        End Sub
    End Class

    ''' <summary>
    ''' Scans at the dimension and appearance stations. Unlike final inspection, a part that fails a
    ''' barcode check is not recorded; the station only records the operator's (or the mode's) judgement.
    ''' </summary>
    Public Class StationInspectionService

        Private ReadOnly _repository As IStationRepository

        Public Sub New(repository As IStationRepository)
            _repository = repository
        End Sub

        ''' <remarks>Only the barcode rules of <paramref name="options"/> are used (format, checksums, running number).</remarks>
        Public Async Function InspectAsync(request As InspectionRequest,
                                           options As InspectionOptions,
                                           mode As JudgementMode,
                                           prompts As IStationPrompts) As Task(Of StationInspectionOutcome)
            Dim outcome As New StationInspectionOutcome(New InspectionData With {
                .ProductName = request.ProductName,
                .WorkOrder = request.WorkOrder,
                .Barcode = request.Barcode,
                .Hinge = If(String.IsNullOrEmpty(request.Hinge), Nothing, request.Hinge),
                .LineNo = request.LineNo,
                .OperatorID = request.OperatorId
            })

            If Await PassesBarcodeChecksAsync(outcome, options, prompts) Then
                Judge(outcome, mode, prompts)
            End If

            Return outcome
        End Function

        Public Function SaveAsync(outcome As StationInspectionOutcome) As Task
            If outcome.Status <> StationScanStatus.Judged Then Throw New InvalidOperationException("Only a judged scan can be saved.")
            Return _repository.InsertAsync(outcome.Inspection)
        End Function

        ' Same order as final inspection: format, hinge format, duplicate, then checksums.
        Private Async Function PassesBarcodeChecksAsync(outcome As StationInspectionOutcome, options As InspectionOptions, prompts As IStationPrompts) As Task(Of Boolean)
            Dim item = outcome.Inspection
            Dim barcode = item.Barcode

            Dim failure = FormatFailure(item, options)
            If failure Is Nothing AndAlso Await _repository.ExistsAsync(barcode) Then
                failure = New InspectionFailure(barcode, "Duplicate barcode detected", FailurePriority.Critical)
            End If
            If failure Is Nothing Then failure = ChecksumFailure(barcode, options)

            If failure IsNot Nothing Then
                outcome.Failures.Add(failure)
                outcome.Status = StationScanStatus.Rejected
                Return False
            End If

            If options.CheckRunningNo AndAlso Not String.IsNullOrWhiteSpace(options.RunningNoRange) Then
                Dim rangeFrom, rangeTo, digitCount As Integer
                If BarcodeRules.TryParseRunningNoRange(options.RunningNoRange, rangeFrom, rangeTo, digitCount) AndAlso
                   Not BarcodeRules.IsRunningNoInRange(barcode, rangeFrom, rangeTo, digitCount) Then

                    Select Case prompts.AskRunningNoDecision(options.RunningNoRange.Trim(), barcode)
                        Case RunningNoDecision.Rework
                            item.Remark = "Rework/Scrap"
                        Case RunningNoDecision.Reject
                            outcome.Status = StationScanStatus.Rejected
                            Return False
                    End Select
                End If
            End If

            Return True
        End Function

        Private Shared Function FormatFailure(item As InspectionData, options As InspectionOptions) As InspectionFailure
            Dim rule = BarcodeRules.CheckBarcodeFormat(item.Barcode, options.BarcodeTemplate)
            If Not rule.IsValid Then Return New InspectionFailure(item.Barcode, rule.Message, FailurePriority.BarcodeFormat)

            If Not String.IsNullOrEmpty(item.Hinge) Then
                rule = BarcodeRules.CheckHingeFormat(item.Hinge, options.HingeTemplate)
                If Not rule.IsValid Then Return New InspectionFailure(item.Hinge, rule.Message, FailurePriority.HingeFormat)
            End If

            Return Nothing
        End Function

        Private Shared Function ChecksumFailure(barcode As String, options As InspectionOptions) As InspectionFailure
            If options.CheckChecksum34 Then
                Dim rule = BarcodeRules.CheckChecksum34(barcode)
                If Not rule.IsValid Then Return New InspectionFailure(barcode, rule.Message, FailurePriority.Checksum34)
            End If

            If options.CheckChecksum36 Then
                Dim rule = BarcodeRules.CheckChecksum36(barcode)
                If Not rule.IsValid Then Return New InspectionFailure(barcode, rule.Message, FailurePriority.Checksum36)
            End If

            Return Nothing
        End Function

        Private Shared Sub Judge(outcome As StationInspectionOutcome, mode As JudgementMode, prompts As IStationPrompts)
            Dim item = outcome.Inspection

            Select Case mode
                Case JudgementMode.AlwaysPass
                    item.IsPass = True

                Case JudgementMode.AlwaysFail
                    If String.IsNullOrEmpty(item.Remark) Then
                        Dim reason = prompts.AskFailReason()
                        If reason Is Nothing Then
                            outcome.Status = StationScanStatus.Cancelled
                            Return
                        End If
                        item.Remark = reason
                    End If
                    item.IsPass = False

                Case Else
                    Dim judgement = prompts.AskJudgement(item.Barcode)
                    If judgement Is Nothing Then
                        outcome.Status = StationScanStatus.Cancelled
                        Return
                    End If
                    item.IsPass = judgement.IsPass
                    ' A running-number remark ("Rework/Scrap") takes priority over the operator's remark.
                    If String.IsNullOrEmpty(item.Remark) Then item.Remark = judgement.Remark
            End Select

            outcome.Status = StationScanStatus.Judged
        End Sub

    End Class

End Namespace
