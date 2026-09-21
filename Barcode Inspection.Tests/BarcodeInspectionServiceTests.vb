Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class BarcodeInspectionServiceTests

    ''' <summary>In-memory stand-in for the database.</summary>
    Private Class FakeRepository
        Implements IInspectionRepository

        Public ReadOnly PassedBarcodes As New HashSet(Of String)
        Public ReadOnly DimensionBarcodes As New HashSet(Of String)
        Public ReadOnly Saved As New List(Of InspectionData)
        Public Queries As Integer

        Public Function PassRecordExistsAsync(barcode As String) As Task(Of Boolean) Implements IInspectionRepository.PassRecordExistsAsync
            Queries += 1
            Return Task.FromResult(PassedBarcodes.Contains(barcode))
        End Function

        Public Function PassRecordWithPrefixExistsAsync(prefix As String) As Task(Of Boolean) Implements IInspectionRepository.PassRecordWithPrefixExistsAsync
            Queries += 1
            Return Task.FromResult(PassedBarcodes.Any(Function(b) b.StartsWith(prefix, StringComparison.Ordinal)))
        End Function

        Public Function DimensionRecordExistsAsync(barcode As String) As Task(Of Boolean) Implements IInspectionRepository.DimensionRecordExistsAsync
            Queries += 1
            Return Task.FromResult(DimensionBarcodes.Contains(barcode))
        End Function

        Public Function AppearanceRecordExistsAsync(barcode As String) As Task(Of Boolean) Implements IInspectionRepository.AppearanceRecordExistsAsync
            Queries += 1
            Return Task.FromResult(False)
        End Function

        Public Function SaveAsync(inspection As InspectionData) As Task Implements IInspectionRepository.SaveAsync
            Saved.Add(inspection)
            Return Task.CompletedTask
        End Function
    End Class

    Private _repository As FakeRepository
    Private _service As BarcodeInspectionService

    <TestInitialize>
    Public Sub SetUp()
        _repository = New FakeRepository()
        _service = New BarcodeInspectionService(_repository)
    End Sub

    Private Shared Function Request(barcode As String, Optional hinge As String = "") As InspectionRequest
        Return New InspectionRequest With {
            .ProductName = "P1", .WorkOrder = "WO1", .Barcode = barcode, .Hinge = hinge,
            .CartonNo = 1, .LineNo = 2, .OperatorId = "OP1"}
    End Function

    Private Shared Function Options(Optional template As String = "K7L@@@@") As InspectionOptions
        Return New InspectionOptions With {.BarcodeTemplate = template}
    End Function

    Private Shared Function NeverAsked(rangeText As String, barcode As String) As RunningNoDecision
        Assert.Fail("Operator should not have been asked about the running number.")
        Return RunningNoDecision.None
    End Function

    <TestMethod>
    Public Async Function ValidBarcode_Passes() As Task
        Dim outcome = Await _service.InspectAsync(Request("K7L0001"), Options(), AddressOf NeverAsked)

        Assert.IsTrue(outcome.Inspection.IsPass)
        Assert.AreEqual(0, outcome.Failures.Count)
        Assert.IsNull(outcome.Inspection.Hinge, "An empty hinge is stored as NULL.")
    End Function

    <TestMethod>
    Public Async Function FormatFailure_StopsBeforeTouchingTheDatabase() As Task
        Dim outcome = Await _service.InspectAsync(Request("X7L0001"), Options(), AddressOf NeverAsked)

        Assert.IsFalse(outcome.Inspection.IsPass)
        Assert.AreEqual("Barcode format mismatch", outcome.Inspection.Remark)
        Assert.AreEqual(FailurePriority.BarcodeFormat, outcome.Failures.Single().Priority)
        Assert.AreEqual(0, _repository.Queries)
    End Function

    <TestMethod>
    Public Async Function AlreadyPassedBarcode_IsDuplicate() As Task
        _repository.PassedBarcodes.Add("K7L0001")

        Dim outcome = Await _service.InspectAsync(Request("K7L0001"), Options(), AddressOf NeverAsked)

        Assert.AreEqual("Duplicate barcode", outcome.Inspection.Remark)
        Assert.AreEqual("Duplicate barcode detected", outcome.Failures.Single().Message)
    End Function

    <TestMethod>
    Public Async Function BarcodeWithSuffix_IsDuplicateOfItsBase() As Task
        _repository.PassedBarcodes.Add("K7L0001+A")

        Dim outcome = Await _service.InspectAsync(Request("K7L0001+B"), Options("K7L@@@@@@"), AddressOf NeverAsked)

        Assert.AreEqual("Duplicate base barcode detected", outcome.Failures.Single().Message)
    End Function

    <TestMethod>
    Public Async Function MissingDimensionRecord_Fails() As Task
        Dim opts = Options()
        opts.CheckDimension = True

        Dim outcome = Await _service.InspectAsync(Request("K7L0001"), opts, AddressOf NeverAsked)

        Assert.AreEqual(False, outcome.Inspection.DimensionResult)
        Assert.AreEqual("Dimension result Not found", outcome.Inspection.Remark)
    End Function

    <TestMethod>
    Public Async Function BarcodeHingeMismatch_Fails() As Task
        Dim opts = Options()
        opts.CheckBarcodeMatchesHinge = True

        Dim outcome = Await _service.InspectAsync(Request("K7L0001", hinge:="K7L0002"), opts, AddressOf NeverAsked)

        Assert.AreEqual("Barcode vs Hinge mismatch", outcome.Inspection.Remark)
        Assert.AreEqual("K7L0001 vs K7L0002", outcome.Failures.Single().Subject)
    End Function

    <TestMethod>
    Public Async Function RunningNoOutOfRange_OperatorRejects() As Task
        Dim opts = Options()
        opts.CheckRunningNo = True
        opts.RunningNoRange = "0001-0100"

        Dim outcome = Await _service.InspectAsync(Request("K7L0200"), opts, Function(range, barcode) RunningNoDecision.Reject)

        Assert.IsFalse(outcome.Inspection.IsPass)
        Assert.AreEqual("Rejected running no.", outcome.Inspection.Remark)
    End Function

    <TestMethod>
    Public Async Function RunningNoOutOfRange_ReworkStillPasses() As Task
        Dim opts = Options()
        opts.CheckRunningNo = True
        opts.RunningNoRange = "0001-0100"

        Dim outcome = Await _service.InspectAsync(Request("K7L0200"), opts, Function(range, barcode) RunningNoDecision.Rework)

        Assert.IsTrue(outcome.Inspection.IsPass)
        Assert.AreEqual("Rework/Scrap", outcome.Inspection.Remark)
    End Function

    <TestMethod>
    Public Async Function Save_StoresTheInspection() As Task
        Dim outcome = Await _service.InspectAsync(Request("K7L0001"), Options(), AddressOf NeverAsked)

        Await _service.SaveAsync(outcome, Options())

        Assert.AreSame(outcome.Inspection, _repository.Saved.Single())
    End Function

    <DataTestMethod>
    <DataRow(False, False, "AVI ST1 and ST2 failed")>
    <DataRow(False, True, "AVI ST1 failed")>
    <DataRow(True, False, "AVI ST2 failed")>
    Public Sub AviFailure_IsDescribedPerStation(st1 As Boolean, st2 As Boolean, expected As String)
        Assert.AreEqual(expected, BarcodeInspectionService.DescribeAviFailure(st1, st2))
    End Sub

End Class
