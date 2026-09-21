Option Strict On

Namespace Domain

    ''' <summary>
    ''' Which checks are enabled for the selected product, and where their input files live.
    ''' Built from the UI once per scan so the inspection logic never reads controls directly.
    ''' </summary>
    Public Class InspectionOptions
        Public Property BarcodeTemplate As String = ""
        Public Property HingeTemplate As String = ""

        Public Property CheckChecksum34 As Boolean
        Public Property CheckChecksum36 As Boolean
        Public Property CheckDimension As Boolean
        Public Property CheckElectrical As Boolean
        Public Property CheckAppearance As Boolean
        Public Property CheckHingeData As Boolean
        Public Property CheckBarcodeMatchesHinge As Boolean
        Public Property CheckAvi As Boolean
        Public Property CheckRunningNo As Boolean

        Public Property RunningNoRange As String = ""

        Public Property ElectricalInputFolder As String = ""
        Public Property ElectricalOutputFolder As String = ""
        Public Property HingeFolder As String = ""
        Public Property AviStation1Folder As String = ""
        Public Property AviStation2Folder As String = ""
    End Class

End Namespace
