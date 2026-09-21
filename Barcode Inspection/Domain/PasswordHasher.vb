Option Strict On

Imports System.Security.Cryptography

Namespace Domain

    ''' <summary>
    ''' Salted PBKDF2-SHA256 password hashes, stored as "pbkdf2-sha256$iterations$salt$hash" (salt and hash in Base64).
    ''' The same idea as bcrypt: only the hash is stored, and it cannot be turned back into the password.
    ''' </summary>
    Public NotInheritable Class PasswordHasher

        Private Const Scheme As String = "pbkdf2-sha256"
        Private Const DefaultIterations As Integer = 100000
        Private Const SaltBytes As Integer = 16
        Private Const HashBytes As Integer = 32

        Private Sub New()
        End Sub

        Public Shared Function Hash(password As String) As String
            Dim salt(SaltBytes - 1) As Byte
            Using random = RandomNumberGenerator.Create()
                random.GetBytes(salt)
            End Using
            Return Format(DefaultIterations, salt, Derive(password, salt, DefaultIterations))
        End Function

        ''' <summary>True when <paramref name="password"/> matches <paramref name="stored"/>. A malformed hash never matches.</summary>
        Public Shared Function Verify(password As String, stored As String) As Boolean
            If password Is Nothing OrElse String.IsNullOrEmpty(stored) Then Return False

            Dim parts = stored.Split("$"c)
            Dim iterations As Integer
            If parts.Length <> 4 OrElse parts(0) <> Scheme OrElse Not Integer.TryParse(parts(1), iterations) OrElse iterations <= 0 Then Return False

            Dim salt, expected As Byte()
            Try
                salt = Convert.FromBase64String(parts(2))
                expected = Convert.FromBase64String(parts(3))
            Catch ex As FormatException
                Return False
            End Try

            Return FixedTimeEquals(Derive(password, salt, iterations, expected.Length), expected)
        End Function

        Private Shared Function Derive(password As String, salt As Byte(), iterations As Integer, Optional length As Integer = HashBytes) As Byte()
            Using pbkdf2 As New Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256)
                Return pbkdf2.GetBytes(length)
            End Using
        End Function

        Private Shared Function Format(iterations As Integer, salt As Byte(), hashValue As Byte()) As String
            Return $"{Scheme}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hashValue)}"
        End Function

        ''' <summary>Compares every byte so the time taken does not reveal how much of the hash matched.</summary>
        Private Shared Function FixedTimeEquals(a As Byte(), b As Byte()) As Boolean
            If a.Length <> b.Length Then Return False
            Dim difference = 0
            For i As Integer = 0 To a.Length - 1
                difference = difference Or (a(i) Xor b(i))
            Next
            Return difference = 0
        End Function

    End Class

    Public NotInheritable Class AdminPasswordRules

        Public Const MinimumLength As Integer = 8

        Private Sub New()
        End Sub

        ''' <summary>Returns why the new password is not acceptable, or Nothing when it is.</summary>
        Public Shared Function CheckNewPassword(currentPassword As String, newPassword As String, confirmation As String) As String
            If String.IsNullOrEmpty(newPassword) OrElse newPassword.Length < MinimumLength Then
                Return $"The new password must be at least {MinimumLength} characters."
            End If
            If newPassword <> confirmation Then Return "The new password and its confirmation do not match."
            If newPassword = currentPassword Then Return "The new password must be different from the current one."
            Return Nothing
        End Function

    End Class

End Namespace
