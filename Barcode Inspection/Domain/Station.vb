Option Strict On

Namespace Domain

    ''' <summary>An inspection station that records its own pass/fail result before final inspection.</summary>
    Public Enum Station
        Dimension
        Appearance
    End Enum

    ''' <summary>How a station decides pass/fail once the barcode checks have passed.</summary>
    Public Enum JudgementMode
        ''' <summary>The operator chooses PASS or FAIL for every part.</summary>
        Manual = 1
        ''' <summary>Every part that passes the barcode checks is recorded as PASS.</summary>
        AlwaysPass = 2
        ''' <summary>Every part is recorded as FAIL, with a reason from the operator.</summary>
        AlwaysFail = 3
    End Enum

    ''' <summary>
    ''' Everything that differs between the stations. Table and column names come only from here,
    ''' never from user input, so they are safe to put into SQL text.
    ''' </summary>
    Public NotInheritable Class StationInfo

        Public ReadOnly Property Station As Station
        Public ReadOnly Property DisplayName As String
        Public ReadOnly Property TableName As String
        Public ReadOnly Property ResultColumn As String
        ''' <summary>Name of the user setting that stores the <see cref="JudgementMode"/>.</summary>
        Public ReadOnly Property ModeSettingName As String

        Private Sub New(kind As Station, displayName As String, tableName As String, resultColumn As String, modeSettingName As String)
            Me.Station = kind
            Me.DisplayName = displayName
            Me.TableName = tableName
            Me.ResultColumn = resultColumn
            Me.ModeSettingName = modeSettingName
        End Sub

        Public Shared ReadOnly Dimension As New StationInfo(Domain.Station.Dimension, "Dimension", "record_barcode_dimension", "dimension_result", "dim_mode")
        Public Shared ReadOnly Appearance As New StationInfo(Domain.Station.Appearance, "Appearance", "record_barcode_appearance", "appearance_result", "app_mode")

        Public Shared Function [For](kind As Station) As StationInfo
            Return If(kind = Domain.Station.Dimension, Dimension, Appearance)
        End Function

        ''' <summary>Reads the saved setting ("1", "2", "3"). Anything else means manual judgement.</summary>
        Public Shared Function ParseMode(settingValue As String) As JudgementMode
            Select Case settingValue
                Case "2" : Return JudgementMode.AlwaysPass
                Case "3" : Return JudgementMode.AlwaysFail
                Case Else : Return JudgementMode.Manual
            End Select
        End Function

    End Class

    ''' <summary>Search conditions for the station report tabs. Nothing means "any".</summary>
    Public Class StationSearchFilter
        Public Property ProductName As String
        Public Property WorkOrder As String
        Public Property DateFrom As Date?
        Public Property DateTo As Date?
        Public Property Barcode As String
        Public Property Hinge As String
    End Class

End Namespace
