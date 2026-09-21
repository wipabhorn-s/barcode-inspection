Option Strict On

Imports Barcode_Inspection.Domain

Namespace Data

    ''' <summary>
    ''' Database operations the inspection service needs. Depending on this interface instead of the
    ''' concrete repository lets the service be tested without a PostgreSQL server.
    ''' </summary>
    Public Interface IInspectionRepository
        Function PassRecordExistsAsync(barcode As String) As Task(Of Boolean)
        Function PassRecordWithPrefixExistsAsync(prefix As String) As Task(Of Boolean)
        Function DimensionRecordExistsAsync(barcode As String) As Task(Of Boolean)
        Function AppearanceRecordExistsAsync(barcode As String) As Task(Of Boolean)
        Function SaveAsync(inspection As InspectionData) As Task
    End Interface

End Namespace
