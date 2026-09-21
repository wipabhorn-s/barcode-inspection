Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Text.RegularExpressions
Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain
Imports Barcode_Inspection.Services
Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class PasswordHasherTests

    <TestMethod>
    Public Sub Hash_VerifiesOnlyTheSamePassword()
        Dim stored = PasswordHasher.Hash("secret-123")

        Assert.IsTrue(PasswordHasher.Verify("secret-123", stored))
        Assert.IsFalse(PasswordHasher.Verify("secret-124", stored))
        Assert.IsFalse(PasswordHasher.Verify("", stored))
    End Sub

    <TestMethod>
    Public Sub Hash_IsSaltedAndDoesNotContainThePassword()
        Dim first = PasswordHasher.Hash("secret-123")
        Dim second = PasswordHasher.Hash("secret-123")

        Assert.AreNotEqual(first, second, "a random salt makes every hash different")
        Assert.IsFalse(first.Contains("secret-123"))
        StringAssert.StartsWith(first, "pbkdf2-sha256$100000$")
    End Sub

    <DataTestMethod>
    <DataRow("")>
    <DataRow("plain-text-password")>
    <DataRow("pbkdf2-sha256$abc$AAAA$AAAA")>
    <DataRow("pbkdf2-sha256$1000$not-base64$AAAA")>
    <DataRow("md5$1000$AAAA$AAAA")>
    Public Sub MalformedHash_NeverMatches(stored As String)
        Assert.IsFalse(PasswordHasher.Verify("anything", stored))
    End Sub

    ''' <summary>The hash in the setup script was generated outside .NET; it must verify here as "12345678".</summary>
    <TestMethod>
    Public Sub SetupScripts_StartWithPassword12345678()
        For Each script In {"create_schema.sql", "002_admin_password.sql"}
            Dim sql = File.ReadAllText(Path.Combine(DatabaseFolder(), script))
            Dim stored = Regex.Match(sql, "'(pbkdf2-sha256\$[^']+)'").Groups(1).Value

            Assert.IsTrue(PasswordHasher.Verify("12345678", stored), script)
            Assert.IsFalse(PasswordHasher.Verify("87654321", stored), script & ": any other password must fail")
        Next
    End Sub

    Private Shared Function DatabaseFolder(<CallerFilePath> Optional thisFile As String = "") As String
        Return Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(thisFile)), "Database")
    End Function

End Class

<TestClass>
Public Class AdminPasswordServiceTests

    Private Class InMemorySettings
        Implements IAppSettingRepository

        Public ReadOnly Values As New Dictionary(Of String, String)

        Public Function GetAsync(key As String) As Task(Of String) Implements IAppSettingRepository.GetAsync
            Dim value As String = Nothing
            Values.TryGetValue(key, value)
            Return Task.FromResult(value)
        End Function

        Public Function SetAsync(key As String, value As String) As Task Implements IAppSettingRepository.SetAsync
            Values(key) = value
            Return Task.CompletedTask
        End Function
    End Class

    Private _settings As InMemorySettings
    Private _service As AdminPasswordService

    <TestInitialize>
    Public Sub SetUp()
        _settings = New InMemorySettings()
        _settings.Values(AdminPasswordService.SettingKey) = PasswordHasher.Hash("12345678")
        _service = New AdminPasswordService(_settings)
    End Sub

    <TestMethod>
    Public Async Function Check_ComparesAgainstTheStoredHash() As Task
        Assert.AreEqual(PasswordCheck.Correct, Await _service.CheckAsync("12345678"))
        Assert.AreEqual(PasswordCheck.Incorrect, Await _service.CheckAsync("87654321"))
    End Function

    <TestMethod>
    Public Async Function Check_WithoutStoredHash_IsNotConfigured() As Task
        _settings.Values.Clear()
        Assert.AreEqual(PasswordCheck.NotConfigured, Await _service.CheckAsync("12345678"), "no hidden default password")
    End Function

    <TestMethod>
    Public Async Function Change_ReplacesThePassword() As Task
        Assert.IsNull(Await _service.ChangeAsync("12345678", "new-pass-99", "new-pass-99"))

        Assert.AreEqual(PasswordCheck.Correct, Await _service.CheckAsync("new-pass-99"))
        Assert.AreEqual(PasswordCheck.Incorrect, Await _service.CheckAsync("12345678"))
        Assert.IsFalse(_settings.Values(AdminPasswordService.SettingKey).Contains("new-pass-99"), "only the hash is stored")
    End Function

    <DataTestMethod>
    <DataRow("wrong-old", "new-pass-99", "new-pass-99", "The current password is incorrect.")>
    <DataRow("12345678", "short", "short", "The new password must be at least 8 characters.")>
    <DataRow("12345678", "new-pass-99", "new-pass-98", "The new password and its confirmation do not match.")>
    <DataRow("12345678", "12345678", "12345678", "The new password must be different from the current one.")>
    Public Async Function Change_RefusesBadInput(current As String, newPassword As String, confirmation As String, expected As String) As Task
        Assert.AreEqual(expected, Await _service.ChangeAsync(current, newPassword, confirmation))
        Assert.AreEqual(PasswordCheck.Correct, Await _service.CheckAsync("12345678"), "password unchanged")
    End Function

End Class

''' <summary>The app_setting table on a real PostgreSQL; see <see cref="DatabaseIntegrationTests"/>.</summary>
<TestClass>
<TestCategory("Integration")>
Public Class AdminPasswordIntegrationTests

    Private _repository As AppSettingRepository
    Private _original As String

    <TestInitialize>
    Public Async Function SetUp() As Task
        Dim connectionString = Environment.GetEnvironmentVariable("BARCODE_INSPECTION_TEST_DB")
        If String.IsNullOrWhiteSpace(connectionString) Then Assert.Inconclusive("BARCODE_INSPECTION_TEST_DB is not set.")
        _repository = New AppSettingRepository(New DbConnectionFactory(connectionString))
        _original = Await _repository.GetAsync(AdminPasswordService.SettingKey)
    End Function

    <TestCleanup>
    Public Async Function CleanUp() As Task
        If _original IsNot Nothing Then Await _repository.SetAsync(AdminPasswordService.SettingKey, _original)
    End Function

    <TestMethod>
    Public Async Function FreshDatabase_AcceptsInitialPasswordAndCanChangeIt() As Task
        Dim service As New AdminPasswordService(_repository)

        Assert.AreEqual(PasswordCheck.Correct, Await service.CheckAsync("12345678"))
        Assert.IsNull(Await service.ChangeAsync("12345678", "changed-99", "changed-99"))
        Assert.AreEqual(PasswordCheck.Correct, Await service.CheckAsync("changed-99"))
        Assert.AreEqual(PasswordCheck.Incorrect, Await service.CheckAsync("12345678"))
    End Function

End Class
