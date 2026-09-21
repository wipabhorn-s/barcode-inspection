Imports System.IO
Imports Barcode_Inspection.Services
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class FileReaderTests

    Private _folder As String

    <TestInitialize>
    Public Sub CreateTempFolder()
        _folder = Path.Combine(Path.GetTempPath(), "BarcodeInspectionTests_" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(_folder)
    End Sub

    <TestCleanup>
    Public Sub DeleteTempFolder()
        Directory.Delete(_folder, recursive:=True)
    End Sub

    <DataTestMethod>
    <DataRow("K7L0001_PASS.txt", True)>
    <DataRow("K7L0001_pass.txt", True)>
    <DataRow("K7L0001_FAIL.txt", False)>
    Public Sub ElectricalFileName_IsClassifiedByKeyword(fileName As String, expected As Boolean)
        Assert.AreEqual(expected, ElectricalResultReader.ClassifyFileName(fileName).Value)
    End Sub

    <TestMethod>
    Public Sub ElectricalFileName_WithoutKeywordIsUnclear()
        Assert.IsFalse(ElectricalResultReader.ClassifyFileName("K7L0001.txt").HasValue)
    End Sub

    <TestMethod>
    Public Sub ElectricalCsv_AnyFailLineFailsThePart()
        Assert.IsFalse(ElectricalResultReader.ClassifyCsv({"Result,PASS", "Step 3,FAIL"}).Value)
    End Sub

    <TestMethod>
    Public Sub ElectricalCsv_PassOnlyPasses()
        Assert.IsTrue(ElectricalResultReader.ClassifyCsv({"Header", "Result,PASS"}).Value)
    End Sub

    <TestMethod>
    Public Sub ElectricalCsv_NoKeywordIsUnclear()
        Assert.IsFalse(ElectricalResultReader.ClassifyCsv({"Header", "1.23"}).HasValue)
    End Sub

    <TestMethod>
    Public Async Function Electrical_UsesLatestTxtFile() As Task
        File.WriteAllText(Path.Combine(_folder, "K7L0001_FAIL.txt"), "")
        File.SetLastWriteTime(Path.Combine(_folder, "K7L0001_FAIL.txt"), Date.Now.AddMinutes(-5))
        File.WriteAllText(Path.Combine(_folder, "K7L0001_PASS.txt"), "")

        Dim result = Await New ElectricalResultReader().CheckAsync(_folder, "K7L0001")

        Assert.IsTrue(result.IsPass)
        StringAssert.EndsWith(result.TxtFilePath, "K7L0001_PASS.txt")
    End Function

    <TestMethod>
    Public Async Function Electrical_MissingFileFails() As Task
        Dim result = Await New ElectricalResultReader().CheckAsync(_folder, "K7L0001")

        Assert.IsFalse(result.IsPass)
        Assert.AreEqual("Electrical file not found", result.FailureMessage)
    End Function

    <TestMethod>
    Public Sub Hinge_ParsesKnownColumns()
        ' Parsing follows the machine's culture; pin it so the test gives the same result everywhere.
        Dim originalCulture = Threading.Thread.CurrentThread.CurrentCulture
        Threading.Thread.CurrentThread.CurrentCulture = Globalization.CultureInfo.InvariantCulture
        Dim data As HingeData
        Try
            data = HingeFileReader.Parse(
                "Serial,Max(15-120),Avg(15-120),Max(120-15),Avg(120-15),Angle,Clutch Vendor,Clutch DC",
                "H1,1.5,1.2,1.4,1.1,118.5,ACME,2026-01-15")
        Finally
            Threading.Thread.CurrentThread.CurrentCulture = originalCulture
        End Try

        Assert.AreEqual(1.5D, data.Max15.Value)
        Assert.AreEqual(1.1D, data.Avg120.Value)
        Assert.AreEqual(118.5D, data.Angle.Value)
        Assert.AreEqual("ACME", data.Vendor)
        Assert.AreEqual(New Date(2026, 1, 15), data.DateCode.Value)
        Assert.IsTrue(data.IsComplete)
    End Sub

    <TestMethod>
    Public Sub Hinge_MissingTorqueValueIsIncomplete()
        Dim data = HingeFileReader.Parse("Max(15-120),Avg(15-120),Max(120-15),Avg(120-15)", "1.5,,1.4,1.1")

        Assert.IsFalse(data.Avg15.HasValue)
        Assert.IsFalse(data.IsComplete)
    End Sub

    <TestMethod>
    Public Sub Hinge_MissingFileIsReported()
        Dim result = New HingeFileReader().Read(_folder, "H1")

        Assert.IsNull(result.Data)
        Assert.AreEqual("Hinge file not found", result.ErrorMessage)
    End Sub

    <TestMethod>
    Public Async Function Avi_BothStationsMustPass() As Task
        Dim st1 = Directory.CreateDirectory(Path.Combine(_folder, "st1")).FullName
        Dim st2 = Directory.CreateDirectory(Path.Combine(_folder, "st2")).FullName
        File.WriteAllText(Path.Combine(st1, "K7L0001_PASS.csv"), "")
        File.WriteAllText(Path.Combine(st2, "K7L0001_NG.csv"), "")

        Dim result = Await New AviResultReader().CheckAsync("K7L0001", st1, st2)

        Assert.IsTrue(result.Station1Pass)
        Assert.IsFalse(result.Station2Pass)
        CollectionAssert.AreEqual({"AVI ST2 failed"}, result.FailureMessages)
    End Function

End Class
