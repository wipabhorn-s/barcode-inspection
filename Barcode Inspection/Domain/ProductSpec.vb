Option Strict On

Namespace Domain

    ''' <summary>A product's barcode format, carton size and the checks it requires.</summary>
    Public Class ProductSpec
        Public Property ProductName As String
        Public Property ItemCode As String = ""
        Public Property BarcodeSpec As String = ""
        Public Property HingeSpec As String = ""
        Public Property Quantity As Integer
        Public Property Modulus34 As Boolean
        Public Property Modulus36 As Boolean
        Public Property DimensionCheck As Boolean
        Public Property ElectricalCheck As Boolean
        Public Property AppearanceCheck As Boolean
        Public Property HingeCheck As Boolean
        Public Property CmosCheck As Boolean
        Public Property CompareCheck As Boolean
        Public Property MachineCheck As Boolean
        Public Property RunningCheck As Boolean
    End Class

End Namespace
