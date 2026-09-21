Option Strict On

Imports Barcode_Inspection.Data

Namespace Services

    Public Class CombineRequest
        Public Property ProductName As String
        Public Property CartonNo As Integer
        ''' <summary>Parts per carton from the product spec; 0 means no limit is known.</summary>
        Public Property Capacity As Integer
        Public Property Barcode As String
        Public Property OperatorId As String
    End Class

    Public Class CombineResult
        Public Property Success As Boolean
        ''' <summary>Why the part was refused; Nothing on success.</summary>
        Public Property FailureMessage As String

        Public Shared Function Refused(message As String) As CombineResult
            Return New CombineResult With {.Success = False, .FailureMessage = message}
        End Function
    End Class

    ''' <summary>
    ''' Packs passed parts from different work orders into one "combined" carton.
    ''' A part must have passed final inspection, belong to the carton's product, not already be in a
    ''' combined carton, and the carton must not be full.
    ''' </summary>
    Public Class CombinedCartonService

        Private ReadOnly _repository As ICombinedCartonRepository

        Public Sub New(repository As ICombinedCartonRepository)
            _repository = repository
        End Sub

        Public Async Function AddAsync(request As CombineRequest) As Task(Of CombineResult)
            Dim part = Await _repository.FindPassedPartAsync(request.Barcode)
            If part Is Nothing Then Return CombineResult.Refused("Barcode has not been scanned yet")

            If Await _repository.IsCombinedAsync(request.Barcode) Then Return CombineResult.Refused("Barcode has already been combined")

            If part.ProductName <> request.ProductName Then Return CombineResult.Refused($"Barcode belongs to '{part.ProductName}'")

            ' Checked against the database, not the screen, so two stations cannot overfill one carton.
            If request.Capacity > 0 AndAlso Await _repository.CountInCartonAsync(request.ProductName, request.CartonNo) >= request.Capacity Then
                Return CombineResult.Refused("Combined carton is full")
            End If

            Dim inserted = Await _repository.InsertAsync(New CombinedEntry With {
                .CartonNo = request.CartonNo,
                .ProductName = request.ProductName,
                .WorkOrder = part.WorkOrder,
                .Barcode = request.Barcode,
                .OperatorId = request.OperatorId
            })

            If Not inserted Then Return CombineResult.Refused("Barcode has already been combined")
            Return New CombineResult With {.Success = True}
        End Function

    End Class

End Namespace
