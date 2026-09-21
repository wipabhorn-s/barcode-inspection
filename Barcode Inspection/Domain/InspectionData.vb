Option Strict On

Namespace Domain

    ''' <summary>One scanned part and the result of every check performed on it.</summary>
    Public Class InspectionData
        Public Property ProductName As String
        Public Property WorkOrder As String
        Public Property Barcode As String
        Public Property Hinge As String
        Public Property CartonNo As Integer
        Public Property LineNo As Integer
        Public Property OperatorID As String
        Public Property IsPass As Boolean = True
        Public Property ElectricalResult As Boolean?
        Public Property DimensionResult As Boolean?
        Public Property AppearanceResult As Boolean?
        Public Property HingeMax15 As Decimal?
        Public Property HingeAvg15 As Decimal?
        Public Property HingeMax120 As Decimal?
        Public Property HingeAvg120 As Decimal?
        Public Property HingeAngle As Decimal?
        Public Property HingeVendor As String
        Public Property HingeDate As Date?
        Public Property MC1Result As Boolean?
        Public Property MC2Result As Boolean?
        Public Property Remark As String

        ''' <summary>Marks the part as failed with the remark saved to the database.</summary>
        Public Sub MarkFailed(remark As String)
            IsPass = False
            Me.Remark = remark
        End Sub
    End Class

End Namespace
