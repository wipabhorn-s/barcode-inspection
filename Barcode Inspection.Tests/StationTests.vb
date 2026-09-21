Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services
Imports Barcode_Inspection.UI
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class StationInspectionServiceTests

    Private Class FakeStationRepository
        Implements IStationRepository

        Public ReadOnly Existing As New HashSet(Of String)
        Public ReadOnly Saved As New List(Of InspectionData)
        Public Queries As Integer

        Public Function ExistsAsync(barcode As String) As Task(Of Boolean) Implements IStationRepository.ExistsAsync
            Queries += 1
            Return Task.FromResult(Existing.Contains(barcode))
        End Function

        Public Function InsertAsync(inspection As InspectionData) As Task Implements IStationRepository.InsertAsync
            Saved.Add(inspection)
            Return Task.CompletedTask
        End Function
    End Class

    ''' <summary>Scripted operator answers; records what was asked.</summary>
    Private Class FakePrompts
        Implements IStationPrompts

        Public RunningNoAnswer As RunningNoDecision = RunningNoDecision.None
        Public FailReason As String = "scratch"
        Public Judgement As StationJudgement = New StationJudgement With {.IsPass = True, .Remark = ""}
        Public ReadOnly Asked As New List(Of String)

        Public Function AskRunningNoDecision(rangeText As String, barcode As String) As RunningNoDecision Implements IStationPrompts.AskRunningNoDecision
            Asked.Add("running")
            Return RunningNoAnswer
        End Function

        Public Function AskFailReason() As String Implements IStationPrompts.AskFailReason
            Asked.Add("reason")
            Return FailReason
        End Function

        Public Function AskJudgement(barcode As String) As StationJudgement Implements IStationPrompts.AskJudgement
            Asked.Add("judgement")
            Return Judgement
        End Function
    End Class

    Private _repository As FakeStationRepository
    Private _prompts As FakePrompts
    Private _service As StationInspectionService

    <TestInitialize>
    Public Sub SetUp()
        _repository = New FakeStationRepository()
        _prompts = New FakePrompts()
        _service = New StationInspectionService(_repository)
    End Sub

    Private Function Scan(barcode As String, mode As JudgementMode, Optional options As InspectionOptions = Nothing) As StationInspectionOutcome
        Dim request = New InspectionRequest With {.ProductName = "P1", .WorkOrder = "WO1", .Barcode = barcode, .LineNo = 1, .OperatorId = "OP"}
        Return _service.InspectAsync(request, If(options, New InspectionOptions With {.BarcodeTemplate = "K7L@@@@"}), mode, _prompts).Result
    End Function

    <TestMethod>
    Public Sub ManualMode_UsesOperatorJudgement()
        _prompts.Judgement = New StationJudgement With {.IsPass = False, .Remark = "dent"}

        Dim outcome = Scan("K7L0001", JudgementMode.Manual)

        Assert.AreEqual(StationScanStatus.Judged, outcome.Status)
        Assert.IsFalse(outcome.Inspection.IsPass)
        Assert.AreEqual("dent", outcome.Inspection.Remark)
    End Sub

    <TestMethod>
    Public Sub ManualMode_CancelSavesNothing()
        _prompts.Judgement = Nothing

        Dim outcome = Scan("K7L0001", JudgementMode.Manual)

        Assert.AreEqual(StationScanStatus.Cancelled, outcome.Status)
        Assert.ThrowsException(Of InvalidOperationException)(Sub() _service.SaveAsync(outcome).Wait())
    End Sub

    <TestMethod>
    Public Sub AlwaysPass_DoesNotAskTheOperator()
        Dim outcome = Scan("K7L0001", JudgementMode.AlwaysPass)

        Assert.IsTrue(outcome.Inspection.IsPass)
        Assert.AreEqual(0, _prompts.Asked.Count)
    End Sub

    <TestMethod>
    Public Sub AlwaysFail_AsksForReason()
        Dim outcome = Scan("K7L0001", JudgementMode.AlwaysFail)

        Assert.IsFalse(outcome.Inspection.IsPass)
        Assert.AreEqual("scratch", outcome.Inspection.Remark)
    End Sub

    <TestMethod>
    Public Sub AlwaysFail_CancelledReasonSavesNothing()
        _prompts.FailReason = Nothing

        Assert.AreEqual(StationScanStatus.Cancelled, Scan("K7L0001", JudgementMode.AlwaysFail).Status)
    End Sub

    <TestMethod>
    Public Sub BadFormat_IsRejectedWithoutQueryingOrAsking()
        Dim outcome = Scan("X7L0001", JudgementMode.Manual)

        Assert.AreEqual(StationScanStatus.Rejected, outcome.Status)
        Assert.AreEqual("Barcode format incorrect", outcome.Failures.Single().Message)
        Assert.AreEqual(0, _repository.Queries)
        Assert.AreEqual(0, _prompts.Asked.Count)
    End Sub

    <TestMethod>
    Public Sub Duplicate_IsReportedBeforeChecksum()
        ' Both duplicate and a bad checksum: the operator must see "duplicate", as before the refactor.
        _repository.Existing.Add("K7L0001")
        Dim options = New InspectionOptions With {.BarcodeTemplate = "K7L@@@@", .CheckChecksum34 = True}

        Dim outcome = Scan("K7L0001", JudgementMode.Manual, options)

        Assert.AreEqual(StationScanStatus.Rejected, outcome.Status)
        Assert.AreEqual("Duplicate barcode detected", outcome.Failures.Single().Message)
    End Sub

    <TestMethod>
    Public Sub RunningNoReject_IsRejectedWithoutMessage()
        _prompts.RunningNoAnswer = RunningNoDecision.Reject
        Dim options = New InspectionOptions With {.BarcodeTemplate = "K7L@@@@", .CheckRunningNo = True, .RunningNoRange = "0001-0100"}

        Dim outcome = Scan("K7L0500", JudgementMode.Manual, options)

        Assert.AreEqual(StationScanStatus.Rejected, outcome.Status)
        Assert.AreEqual(0, outcome.Failures.Count)
        CollectionAssert.AreEqual({"running"}, _prompts.Asked)
    End Sub

    <TestMethod>
    Public Sub RunningNoRework_KeepsReworkRemarkOverOperatorRemark()
        _prompts.RunningNoAnswer = RunningNoDecision.Rework
        _prompts.Judgement = New StationJudgement With {.IsPass = True, .Remark = "ok"}
        Dim options = New InspectionOptions With {.BarcodeTemplate = "K7L@@@@", .CheckRunningNo = True, .RunningNoRange = "0001-0100"}

        Dim outcome = Scan("K7L0500", JudgementMode.Manual, options)

        Assert.IsTrue(outcome.Inspection.IsPass)
        Assert.AreEqual("Rework/Scrap", outcome.Inspection.Remark)
    End Sub

    <TestMethod>
    Public Sub RunningNoRework_InAlwaysFailMode_DoesNotAskForReason()
        _prompts.RunningNoAnswer = RunningNoDecision.Rework
        Dim options = New InspectionOptions With {.BarcodeTemplate = "K7L@@@@", .CheckRunningNo = True, .RunningNoRange = "0001-0100"}

        Dim outcome = Scan("K7L0500", JudgementMode.AlwaysFail, options)

        Assert.IsFalse(outcome.Inspection.IsPass)
        Assert.AreEqual("Rework/Scrap", outcome.Inspection.Remark)
        CollectionAssert.AreEqual({"running"}, _prompts.Asked)
    End Sub

    <TestMethod>
    Public Sub Save_WritesJudgedScan()
        Dim outcome = Scan("K7L0001", JudgementMode.AlwaysPass)

        _service.SaveAsync(outcome).Wait()

        Assert.AreSame(outcome.Inspection, _repository.Saved.Single())
    End Sub

End Class

<TestClass>
Public Class StationQueryTests

    <TestMethod>
    Public Sub StationInfo_MapsEachStationToItsOwnTable()
        Assert.AreEqual("record_barcode_dimension", StationInfo.For(Station.Dimension).TableName)
        Assert.AreEqual("appearance_result", StationInfo.For(Station.Appearance).ResultColumn)
    End Sub

    <DataTestMethod>
    <DataRow("2", JudgementMode.AlwaysPass)>
    <DataRow("3", JudgementMode.AlwaysFail)>
    <DataRow("1", JudgementMode.Manual)>
    <DataRow("", JudgementMode.Manual)>
    <DataRow(Nothing, JudgementMode.Manual)>
    Public Sub ParseMode_DefaultsToManual(setting As String, expected As JudgementMode)
        Assert.AreEqual(expected, StationInfo.ParseMode(setting))
    End Sub

    <TestMethod>
    Public Sub Search_WithNoFilter_OnlySelectsResult()
        Dim query = StationRepository.BuildSearchQuery(StationInfo.Appearance, New StationSearchFilter(), passed:=False)

        StringAssert.Contains(query.Sql, "FROM barcode_inspection.record_barcode_appearance")
        StringAssert.Contains(query.Sql, "WHERE appearance_result = FALSE")
        Assert.AreEqual(0, query.Parameters.Count)
    End Sub

    <TestMethod>
    Public Sub Search_PutsValuesInParameters()
        Dim filter = New StationSearchFilter With {.ProductName = "P1'; DROP TABLE x;--", .Barcode = "K7L0001"}

        Dim query = StationRepository.BuildSearchQuery(StationInfo.Dimension, filter, passed:=True)

        Assert.IsFalse(query.Sql.Contains("DROP TABLE"))
        Assert.AreEqual("P1'; DROP TABLE x;--", query.Parameters("ProductName"))
        Assert.AreEqual("K7L0001", query.Parameters("Barcode"))
        StringAssert.Contains(query.Sql, "AND barcode = @Barcode")
    End Sub

    <TestMethod>
    Public Sub Search_OrdersReversedDateRange()
        Dim filter = New StationSearchFilter With {.DateFrom = New Date(2026, 9, 30), .DateTo = New Date(2026, 9, 1)}

        Dim query = StationRepository.BuildSearchQuery(StationInfo.Dimension, filter, passed:=True)

        Assert.AreEqual(New Date(2026, 9, 1), query.Parameters("DateFrom"))
        Assert.AreEqual(New Date(2026, 9, 30), query.Parameters("DateTo"))
    End Sub

    <TestMethod>
    Public Sub AppendNewRows_SkipsRowsAlreadyShown()
        Dim shown = NewTable(1, 2)
        Dim found = NewTable(2, 3)

        ResultGrid.AppendNewRows(shown, found)

        CollectionAssert.AreEqual({1L, 2L, 3L}, shown.Rows.Cast(Of DataRow)().Select(Function(r) CLng(r("id"))).ToArray())
    End Sub

    Private Shared Function NewTable(ParamArray ids As Long()) As DataTable
        Dim table As New DataTable()
        table.Columns.Add("id", GetType(Long))
        For Each id In ids
            table.Rows.Add(id)
        Next
        Return table
    End Function

End Class

<TestClass>
Public Class CsvTests

    <DataTestMethod>
    <DataRow("plain", "plain")>
    <DataRow("a,b", """a,b""")>
    <DataRow("say ""hi""", """say """"hi""""""")>
    <DataRow("", "")>
    Public Sub Escape_FollowsRfc4180(value As String, expected As String)
        Assert.AreEqual(expected, Csv.Escape(value))
    End Sub

    <TestMethod>
    Public Sub FormatCell_BlankForNullAndIsoDates()
        Assert.AreEqual("", Csv.FormatCell(DBNull.Value))
        Assert.AreEqual("2026-09-21", Csv.FormatCell(New Date(2026, 9, 21, 13, 5, 0)))
    End Sub

End Class
