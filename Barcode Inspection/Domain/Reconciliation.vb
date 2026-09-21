Option Strict On

Namespace Domain

    ''' <summary>A part recorded as passed in final inspection.</summary>
    Public Class PassedRecord
        Public Property ProductName As String
        Public Property PartNo As Integer
        Public Property Barcode As String
        Public Property WorkOrder As String
        Public Property CartonNo As Integer
    End Class

    ''' <summary>Which passed records to compare against the physical scan.</summary>
    Public Class ReconciliationFilter
        Public Property ProductName As String
        Public Property WorkOrder As String
        Public Property CartonNo As Integer?

        ''' <summary>
        ''' Builds a filter from the three boxes on screen. A work order, a carton number, or both are needed;
        ''' a carton number on its own also needs the product (carton numbers repeat across work orders).
        ''' </summary>
        Public Shared Function TryCreate(productText As String, workOrderText As String, cartonText As String,
                                         ByRef filter As ReconciliationFilter, ByRef problem As String) As Boolean
            filter = Nothing
            problem = Nothing

            Dim workOrder = If(String.IsNullOrWhiteSpace(workOrderText), Nothing, workOrderText.Trim())
            Dim product = If(String.IsNullOrWhiteSpace(productText), Nothing, productText.Trim())
            Dim carton As Integer? = Nothing

            If workOrder Is Nothing AndAlso String.IsNullOrWhiteSpace(cartonText) Then
                problem = "Please select at least work order or carton box no."
                Return False
            End If

            If Not String.IsNullOrWhiteSpace(cartonText) Then
                Dim number As Integer
                If Not Integer.TryParse(cartonText.Trim(), number) Then
                    problem = "Invalid carton box no."
                    Return False
                End If
                carton = number
            End If

            If workOrder Is Nothing AndAlso product Is Nothing Then
                problem = "Please select product when filtering by carton box no."
                Return False
            End If

            filter = New ReconciliationFilter With {.ProductName = product, .WorkOrder = workOrder, .CartonNo = carton}
            Return True
        End Function
    End Class

    Public NotInheritable Class Reconciliation

        Private Sub New()
        End Sub

        ''' <summary>Passed records whose barcode was not scanned (compared ignoring case and surrounding spaces).</summary>
        Public Shared Function FindMissing(passed As IEnumerable(Of PassedRecord), scannedBarcodes As IEnumerable(Of String)) As List(Of PassedRecord)
            Dim scanned = New HashSet(Of String)(scannedBarcodes.Select(Function(b) b.Trim()), StringComparer.OrdinalIgnoreCase)
            Return passed.Where(Function(p) Not scanned.Contains(p.Barcode.Trim())).ToList()
        End Function

    End Class

End Namespace
