Imports System.IO
Imports System.Runtime.CompilerServices
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports Npgsql

''' <summary>
''' Loads Database/003_sample_data.sql and walks through the "try it" steps in the README,
''' so the instructions people follow keep working.
''' </summary>
<TestClass>
<TestCategory("Integration")>
Public Class SampleDataTests

    Private _db As DbConnectionFactory

    <TestInitialize>
    Public Sub SetUp()
        Dim connectionString = Environment.GetEnvironmentVariable("BARCODE_INSPECTION_TEST_DB")
        If String.IsNullOrWhiteSpace(connectionString) Then Assert.Inconclusive("BARCODE_INSPECTION_TEST_DB is not set.")
        _db = New DbConnectionFactory(connectionString)
        Execute(File.ReadAllText(ScriptPath("003_sample_data.sql")))
    End Sub

    <TestCleanup>
    Public Sub CleanUp()
        ' Leave the sample data loaded, as a user would have it.
        If _db IsNot Nothing Then Execute(File.ReadAllText(ScriptPath("003_sample_data.sql")))
    End Sub

    Private Sub Execute(sql As String)
        Using conn = _db.TryOpenConnection()
            Using cmd As New NpgsqlCommand(sql, conn)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Private Shared Function ScriptPath(name As String, <CallerFilePath> Optional thisFile As String = "") As String
        Return Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(thisFile)), "Database", name)
    End Function

    Private Async Function ScanAsync(product As String, workOrder As String, carton As Integer, barcode As String,
                                     Optional hinge As String = Nothing,
                                     Optional runningNoAnswer As RunningNoDecision = RunningNoDecision.None) As Task(Of InspectionOutcome)
        Dim spec = Await New ProductSpecRepository(_db).GetAsync(product)
        Dim options = New InspectionOptions With {
            .BarcodeTemplate = spec.BarcodeSpec, .HingeTemplate = spec.HingeSpec,
            .CheckRunningNo = spec.RunningCheck, .RunningNoRange = "00001-00050"}
        Dim request = New InspectionRequest With {
            .ProductName = product, .WorkOrder = workOrder, .CartonNo = carton, .Barcode = barcode, .Hinge = hinge,
            .LineNo = 1, .OperatorId = "OP00001"}

        Dim asked As Boolean = False
        Dim service As New BarcodeInspectionService(New InspectionRepository(_db))
        Dim outcome = Await service.InspectAsync(request, options, Function(range, code)
                                                                        asked = True
                                                                        Return runningNoAnswer
                                                                    End Function)
        If runningNoAnswer <> RunningNoDecision.None Then Assert.IsTrue(asked, "the running-number question should be asked")
        Await service.SaveAsync(outcome, options)
        Return outcome
    End Function

    <TestMethod>
    Public Async Function BasicProduct_OneMoreScanFillsTheCarton() As Task
        Dim repo As New InspectionRepository(_db)
        Assert.AreEqual(4, (Await repo.GetCartonRecordsAsync("DEMO-BASIC", "WO-DEMO-001", 1)).Rows.Count)

        Assert.IsTrue((Await ScanAsync("DEMO-BASIC", "WO-DEMO-001", 1, "DB00005")).Inspection.IsPass)
        Assert.AreEqual(5, (Await repo.GetCartonRecordsAsync("DEMO-BASIC", "WO-DEMO-001", 1)).Rows.Count, "carton is now full (5 of 5)")

        Assert.AreEqual("Duplicate barcode", (Await ScanAsync("DEMO-BASIC", "WO-DEMO-001", 2, "DB00001")).Inspection.Remark)
        Assert.AreEqual("Barcode format mismatch", (Await ScanAsync("DEMO-BASIC", "WO-DEMO-001", 2, "XX12345")).Inspection.Remark)
    End Function

    <TestMethod>
    Public Async Function HingeProduct_NeedsAMatchingHingeFormat() As Task
        Assert.IsTrue((Await ScanAsync("DEMO-HINGE", "WO-DEMO-002", 1, "DH00002", hinge:="H0002")).Inspection.IsPass)
        Assert.AreEqual("Hinge Or CMOS format mismatch", (Await ScanAsync("DEMO-HINGE", "WO-DEMO-002", 1, "DH00003", hinge:="X99")).Inspection.Remark)
    End Function

    <TestMethod>
    Public Async Function RunningProduct_AsksAboutNumbersOutsideTheRange() As Task
        Assert.IsTrue((Await ScanAsync("DEMO-RUNNING", "WO-DEMO-004", 1, "DR00010")).Inspection.IsPass)

        Dim rejected = Await ScanAsync("DEMO-RUNNING", "WO-DEMO-004", 1, "DR00099", runningNoAnswer:=RunningNoDecision.Reject)
        Assert.AreEqual("Rejected running no.", rejected.Inspection.Remark)
    End Function

    <TestMethod>
    Public Async Function CombinedCarton_AcceptsTheListedParts() As Task
        Dim service As New CombinedCartonService(New CombinedCartonRepository(_db))
        Dim add = Function(barcode As String) service.AddAsync(New CombineRequest With {
            .ProductName = "DEMO-BASIC", .CartonNo = 1, .Capacity = 5, .Barcode = barcode, .OperatorId = "OP00006"})

        Assert.IsTrue((Await add("DB00002")).Success)
        Assert.IsTrue((Await add("DB00102")).Success)
        Assert.AreEqual("Barcode has already been combined", (Await add("DB00003")).FailureMessage)
    End Function

    <TestMethod>
    Public Async Function Reconciliation_FindsTheUnscannedParts() As Task
        Dim passed = Await New ReconciliationRepository(_db).GetPassedAsync(New ReconciliationFilter With {.WorkOrder = "WO-DEMO-001", .CartonNo = 1})
        Dim missing = Reconciliation.FindMissing(passed, {"DB00001", "DB00003"})

        CollectionAssert.AreEqual({"DB00002", "DB00004"}, missing.Select(Function(m) m.Barcode).ToArray())
    End Function

    <TestMethod>
    Public Async Function AdminPassword_Is12345678() As Task
        Assert.AreEqual(PasswordCheck.Correct, Await New AdminPasswordService(New AppSettingRepository(_db)).CheckAsync("12345678"))
    End Function

End Class
