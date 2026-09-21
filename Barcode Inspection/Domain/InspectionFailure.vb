Option Strict On

Namespace Domain

    ''' <summary>
    ''' Priority used to pick which error the operator sees first (lower = more important).
    ''' Values match the numbers the application has always passed to <c>Message.ResultFailCase</c>.
    ''' </summary>
    Public Enum FailurePriority
        Critical = 1
        BarcodeFormat = 2
        HingeFormat = 3
        Checksum34 = 4
        Checksum36 = 5
        Dimension = 6
        Electrical = 7
        Appearance = 8
        HingeData = 9
        Avi = 14
    End Enum

    ''' <summary>A reason shown to the operator when a check fails.</summary>
    Public NotInheritable Class InspectionFailure
        Public ReadOnly Property Subject As String
        Public ReadOnly Property Message As String
        Public ReadOnly Property Priority As FailurePriority

        ''' <param name="subject">The scanned value the error refers to (barcode or hinge).</param>
        Public Sub New(subject As String, message As String, priority As FailurePriority)
            Me.Subject = subject
            Me.Message = message
            Me.Priority = priority
        End Sub
    End Class

End Namespace
