Option Strict On

Imports Barcode_Inspection.Data
Imports Barcode_Inspection.Domain

Namespace Services

    Public Enum PasswordCheck
        Correct
        Incorrect
        ''' <summary>No admin password hash in the database yet (the setup script has not been run).</summary>
        NotConfigured
    End Enum

    ''' <summary>
    ''' The admin password (Setting tab, Query Console, deleting records). Only a salted hash is stored,
    ''' in the database, so it is the same on every PC and can be changed without rebuilding the program.
    ''' </summary>
    Public Class AdminPasswordService

        Public Const SettingKey As String = "admin_password_hash"

        ''' <summary>The instance the application uses; set once at start-up.</summary>
        Public Shared Property Current As AdminPasswordService

        Private ReadOnly _settings As IAppSettingRepository

        Public Sub New(settings As IAppSettingRepository)
            _settings = settings
        End Sub

        Public Async Function CheckAsync(password As String) As Task(Of PasswordCheck)
            Dim stored = Await _settings.GetAsync(SettingKey)
            If String.IsNullOrEmpty(stored) Then Return PasswordCheck.NotConfigured
            ' PBKDF2 is slow on purpose (about 0.1 s); keep it off the UI thread.
            Dim matches = Await Task.Run(Function() PasswordHasher.Verify(password, stored))
            Return If(matches, PasswordCheck.Correct, PasswordCheck.Incorrect)
        End Function

        ''' <summary>Changes the password. Returns why it was refused, or Nothing on success.</summary>
        Public Async Function ChangeAsync(currentPassword As String, newPassword As String, confirmation As String) As Task(Of String)
            Dim problem = AdminPasswordRules.CheckNewPassword(currentPassword, newPassword, confirmation)
            If problem IsNot Nothing Then Return problem

            Select Case Await CheckAsync(currentPassword)
                Case PasswordCheck.Incorrect : Return "The current password is incorrect."
                Case PasswordCheck.NotConfigured : Return NotConfiguredMessage
            End Select

            Dim newHash = Await Task.Run(Function() PasswordHasher.Hash(newPassword))
            Await _settings.SetAsync(SettingKey, newHash)
            Return Nothing
        End Function

        Public Const NotConfiguredMessage As String =
            "The admin password has not been set up in the database." & vbCrLf &
            "Run Database\002_admin_password.sql, then try again."

    End Class

End Namespace
