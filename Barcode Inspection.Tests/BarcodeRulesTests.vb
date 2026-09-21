Imports Barcode_Inspection.Domain
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class BarcodeRulesTests

    <TestMethod>
    Public Sub BarcodeFormat_WildcardMatchesAnyCharacter()
        Assert.IsTrue(BarcodeRules.CheckBarcodeFormat("K7L0001", "K7L@@@@").IsValid)
    End Sub

    <TestMethod>
    Public Sub BarcodeFormat_FixedCharacterMustMatch()
        Dim result = BarcodeRules.CheckBarcodeFormat("K8L0001", "K7L@@@@")

        Assert.IsFalse(result.IsValid)
        Assert.AreEqual("Barcode format incorrect", result.Message)
    End Sub

    <TestMethod>
    Public Sub BarcodeFormat_LengthMustMatch()
        Dim result = BarcodeRules.CheckBarcodeFormat("K7L00001", "K7L@@@@")

        Assert.IsFalse(result.IsValid)
        Assert.AreEqual("Barcode length mismatch", result.Message)
    End Sub

    <TestMethod>
    Public Sub HingeFormat_EmptyTemplateAcceptsAnything()
        Assert.IsTrue(BarcodeRules.CheckHingeFormat("whatever", "").IsValid)
        Assert.IsTrue(BarcodeRules.CheckHingeFormat("whatever", "   ").IsValid)
    End Sub

    <TestMethod>
    Public Sub HingeFormat_UsesHingeMessages()
        Assert.AreEqual("Hinge or CMOS length mismatch", BarcodeRules.CheckHingeFormat("H1", "H@@").Message)
        Assert.AreEqual("Hinge or CMOS format incorrect", BarcodeRules.CheckHingeFormat("X12", "H@@").Message)
    End Sub

    <DataTestMethod>
    <DataRow("ABC123Z")>
    <DataRow("K7L00019")>
    Public Sub Checksum34_AcceptsCorrectCheckDigit(barcode As String)
        Assert.IsTrue(BarcodeRules.CheckChecksum34(barcode).IsValid)
    End Sub

    <DataTestMethod>
    <DataRow("ABC123Y", "Checksum modulo-34 failed")>
    <DataRow("ABC123I", "Invalid check digit")>
    <DataRow("ABO123Z", "Invalid character in barcode")>
    <DataRow("Z", "Barcode too short for checksum")>
    Public Sub Checksum34_RejectsWithReason(barcode As String, expectedMessage As String)
        Dim result = BarcodeRules.CheckChecksum34(barcode)

        Assert.IsFalse(result.IsValid)
        Assert.AreEqual(expectedMessage, result.Message)
    End Sub

    <TestMethod>
    Public Sub Base34_SkipsLettersIAndO()
        Assert.AreEqual(17, BarcodeRules.GetBase34Value("H"c))
        Assert.AreEqual(18, BarcodeRules.GetBase34Value("J"c))
        Assert.AreEqual(-1, BarcodeRules.GetBase34Value("I"c))
        Assert.AreEqual(-1, BarcodeRules.GetBase34Value("O"c))
        Assert.AreEqual(33, BarcodeRules.GetBase34Value("Z"c))
    End Sub

    <DataTestMethod>
    <DataRow("ABC1234")>
    <DataRow("K7L0001D")>
    <DataRow("K7L0001d")>
    Public Sub Checksum36_AcceptsCorrectCheckCharacter(barcode As String)
        Assert.IsTrue(BarcodeRules.CheckChecksum36(barcode).IsValid)
    End Sub

    <TestMethod>
    Public Sub Checksum36_ReportsExpectedCharacter()
        Dim result = BarcodeRules.CheckChecksum36("ABC1235")

        Assert.IsFalse(result.IsValid)
        Assert.AreEqual("Checksum modulo-36 failed (expected '4', got '5')", result.Message)
    End Sub

    <TestMethod>
    Public Sub RunningNoRange_ParsesDigitsFromLongerSide()
        Dim rangeFrom, rangeTo, digits As Integer

        Assert.IsTrue(BarcodeRules.TryParseRunningNoRange(" 0001 - 0500 ", rangeFrom, rangeTo, digits))
        Assert.AreEqual(1, rangeFrom)
        Assert.AreEqual(500, rangeTo)
        Assert.AreEqual(4, digits)
    End Sub

    <DataTestMethod>
    <DataRow("")>
    <DataRow("0001")>
    <DataRow("1-2-3")>
    <DataRow("A-B")>
    Public Sub RunningNoRange_RejectsInvalidText(rangeText As String)
        Dim rangeFrom, rangeTo, digits As Integer
        Assert.IsFalse(BarcodeRules.TryParseRunningNoRange(rangeText, rangeFrom, rangeTo, digits))
    End Sub

    <DataTestMethod>
    <DataRow("K7L0001", True)>
    <DataRow("K7L0500", True)>
    <DataRow("K7L0501", False)>
    <DataRow("K7L0000", False)>
    Public Sub RunningNo_ChecksLastDigits(barcode As String, expected As Boolean)
        Assert.AreEqual(expected, BarcodeRules.IsRunningNoInRange(barcode, 1, 500, 4))
    End Sub

    <TestMethod>
    Public Sub RunningNo_AcceptsReversedRange()
        Assert.IsTrue(BarcodeRules.IsRunningNoInRange("K7L0100", 500, 1, 4))
    End Sub

End Class
