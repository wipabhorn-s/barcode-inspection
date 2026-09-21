Option Strict On

Namespace Domain

    Public Enum ProductField
        None
        ProductName
        ItemCode
        BarcodeSpec
        Quantity
    End Enum

    ''' <summary>Checks the Setting tab's product form before it is saved.</summary>
    Public NotInheritable Class ProductSpecValidation

        Public ReadOnly Property Field As ProductField
        Public ReadOnly Property Message As String

        Public ReadOnly Property IsValid As Boolean
            Get
                Return Field = ProductField.None
            End Get
        End Property

        Private Sub New(field As ProductField, message As String)
            Me.Field = field
            Me.Message = message
        End Sub

        Private Shared ReadOnly Valid As New ProductSpecValidation(ProductField.None, Nothing)

        ''' <summary>Returns the first problem, in the order of the fields on screen.</summary>
        Public Shared Function Check(productName As String, itemCode As String, barcodeSpec As String, quantityText As String) As ProductSpecValidation
            If String.IsNullOrWhiteSpace(productName) Then Return New ProductSpecValidation(ProductField.ProductName, "Please enter product name.")
            If String.IsNullOrWhiteSpace(itemCode) Then Return New ProductSpecValidation(ProductField.ItemCode, "Please enter item code.")
            If String.IsNullOrWhiteSpace(barcodeSpec) Then Return New ProductSpecValidation(ProductField.BarcodeSpec, "Please enter barcode spec.")
            If String.IsNullOrWhiteSpace(quantityText) Then Return New ProductSpecValidation(ProductField.Quantity, "Please enter quantity per carton.")

            Dim quantity As Integer
            If Not Integer.TryParse(quantityText.Trim(), quantity) OrElse quantity <= 0 Then
                Return New ProductSpecValidation(ProductField.Quantity, "Quantity must be a positive number.")
            End If

            Return Valid
        End Function

    End Class

End Namespace
