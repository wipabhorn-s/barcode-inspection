Option Strict On

Namespace Domain

    ''' <summary>Outcome of a single rule: valid, or invalid with a message for the operator.</summary>
    Public Structure RuleResult
        Public ReadOnly IsValid As Boolean
        Public ReadOnly Message As String

        Private Sub New(isValid As Boolean, message As String)
            Me.IsValid = isValid
            Me.Message = message
        End Sub

        Public Shared ReadOnly Property Ok As RuleResult
            Get
                Return New RuleResult(True, Nothing)
            End Get
        End Property

        Public Shared Function Fail(message As String) As RuleResult
            Return New RuleResult(False, message)
        End Function
    End Structure

    ''' <summary>
    ''' Pure barcode validation rules. Nothing here touches the UI, the database or the file system,
    ''' so every rule can be unit tested on its own.
    ''' </summary>
    Public NotInheritable Class BarcodeRules

        ''' <summary>Character in a format template that matches any character.</summary>
        Public Const Wildcard As Char = "@"c

        Private Const Base34Alphabet As String = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ" ' no I and O

        Private Sub New()
        End Sub

        Public Shared Function CheckBarcodeFormat(barcode As String, template As String) As RuleResult
            Return CheckTemplate(barcode, If(template, ""), "Barcode length mismatch", "Barcode format incorrect")
        End Function

        ''' <summary>An empty hinge template means "any hinge is accepted".</summary>
        Public Shared Function CheckHingeFormat(hinge As String, template As String) As RuleResult
            If String.IsNullOrWhiteSpace(template) Then Return RuleResult.Ok
            Return CheckTemplate(hinge, template, "Hinge or CMOS length mismatch", "Hinge or CMOS format incorrect")
        End Function

        Private Shared Function CheckTemplate(value As String, template As String, lengthMessage As String, formatMessage As String) As RuleResult
            If value.Length <> template.Length Then Return RuleResult.Fail(lengthMessage)

            For i As Integer = 0 To template.Length - 1
                If template(i) <> Wildcard AndAlso template(i) <> value(i) Then Return RuleResult.Fail(formatMessage)
            Next

            Return RuleResult.Ok
        End Function

        ''' <summary>
        ''' Weighted modulo-34 check digit: walking right to left from the character before the check digit,
        ''' odd positions are weighted 3 and even positions 1.
        ''' </summary>
        Public Shared Function CheckChecksum34(barcode As String) As RuleResult
            If barcode.Length < 2 Then Return RuleResult.Fail("Barcode too short for checksum")

            Dim checkValue As Integer = GetBase34Value(barcode(barcode.Length - 1))
            If checkValue < 0 Then Return RuleResult.Fail("Invalid check digit")

            Dim oddSum As Integer = 0
            Dim evenSum As Integer = 0
            Dim isOdd As Boolean = True

            For i As Integer = barcode.Length - 2 To 0 Step -1
                Dim digitValue As Integer = GetBase34Value(barcode(i))
                If digitValue < 0 Then Return RuleResult.Fail("Invalid character in barcode")

                If isOdd Then
                    oddSum += digitValue
                Else
                    evenSum += digitValue
                End If
                isOdd = Not isOdd
            Next

            Dim remainder As Integer = (oddSum * 3 + evenSum) Mod 34
            Dim expected As Integer = If(remainder = 0, 0, 34 - remainder)

            If expected <> checkValue Then Return RuleResult.Fail("Checksum modulo-34 failed")
            Return RuleResult.Ok
        End Function

        ''' <summary>Returns 0-33 for a valid base-34 character (upper case only), otherwise -1.</summary>
        Public Shared Function GetBase34Value(ch As Char) As Integer
            Return Base34Alphabet.IndexOf(ch)
        End Function

        ''' <summary>Check character = (XOR of all data characters) mod 36, written as 0-9 / A-Z.</summary>
        Public Shared Function CheckChecksum36(barcode As String) As RuleResult
            If barcode.Length < 2 Then Return RuleResult.Fail("Barcode too short for checksum")

            Dim dataPart As String = barcode.Substring(0, barcode.Length - 1)
            Dim checkChar As Char = barcode(barcode.Length - 1)

            Dim xorValue As Integer = 0
            For Each ch As Char In dataPart
                xorValue = xorValue Xor Asc(ch)
            Next

            Dim expectedChar As Char = GetBase36Char(xorValue Mod 36)

            If expectedChar <> Char.ToUpper(checkChar) Then
                Return RuleResult.Fail($"Checksum modulo-36 failed (expected '{expectedChar}', got '{checkChar}')")
            End If
            Return RuleResult.Ok
        End Function

        Public Shared Function GetBase36Char(value As Integer) As Char
            If value < 0 OrElse value > 35 Then Throw New ArgumentOutOfRangeException(NameOf(value), "Invalid Base-36 value")
            Return "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"(value)
        End Function

        ''' <summary>Parses a range such as "0001-0500". The digit count is taken from the longer side.</summary>
        Public Shared Function TryParseRunningNoRange(rangeText As String, ByRef rangeFrom As Integer, ByRef rangeTo As Integer, ByRef digitCount As Integer) As Boolean
            rangeFrom = 0 : rangeTo = 0 : digitCount = 0
            If String.IsNullOrWhiteSpace(rangeText) Then Return False

            Dim parts = rangeText.Trim().Split("-"c)
            If parts.Length <> 2 Then Return False

            Dim fromText = parts(0).Trim()
            Dim toText = parts(1).Trim()
            If Not Integer.TryParse(fromText, rangeFrom) OrElse Not Integer.TryParse(toText, rangeTo) Then Return False

            digitCount = Math.Max(fromText.Length, toText.Length)
            Return True
        End Function

        ''' <summary>True when the last <paramref name="digitCount"/> characters of the barcode fall inside the range.</summary>
        Public Shared Function IsRunningNoInRange(barcode As String, rangeFrom As Integer, rangeTo As Integer, digitCount As Integer) As Boolean
            If barcode.Length < digitCount Then Return False

            Dim runningNo As Integer
            If Not Integer.TryParse(barcode.Substring(barcode.Length - digitCount), runningNo) Then Return False

            Return runningNo >= Math.Min(rangeFrom, rangeTo) AndAlso runningNo <= Math.Max(rangeFrom, rangeTo)
        End Function

    End Class

End Namespace
