Option Strict On

Imports Npgsql

Namespace Data

    ''' <summary>Where settings shared by every PC are kept: the barcode_inspection.app_setting table.</summary>
    Public Interface IAppSettingRepository
        ''' <summary>Returns Nothing when the setting does not exist.</summary>
        Function GetAsync(key As String) As Task(Of String)
        Function SetAsync(key As String, value As String) As Task
    End Interface

    Public Class AppSettingRepository
        Implements IAppSettingRepository

        Private ReadOnly _db As DbConnectionFactory

        Public Sub New(db As DbConnectionFactory)
            _db = db
        End Sub

        ''' <exception cref="PostgresException">SqlState 42P01 when the app_setting table has not been created.</exception>
        Public Async Function GetAsync(key As String) As Task(Of String) Implements IAppSettingRepository.GetAsync
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand("SELECT value FROM barcode_inspection.app_setting WHERE key = @key", conn)
                    cmd.Parameters.AddWithValue("@key", key)
                    Dim value = Await cmd.ExecuteScalarAsync().ConfigureAwait(False)
                    Return If(value Is Nothing OrElse TypeOf value Is DBNull, Nothing, value.ToString())
                End Using
            End Using
        End Function

        Public Async Function SetAsync(key As String, value As String) As Task Implements IAppSettingRepository.SetAsync
            Using conn = Await _db.OpenConnectionAsync().ConfigureAwait(False)
                Using cmd As New NpgsqlCommand(
                    "INSERT INTO barcode_inspection.app_setting (key, value, updated_at) VALUES (@key, @value, now())
                     ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now()", conn)
                    cmd.Parameters.AddWithValue("@key", key)
                    cmd.Parameters.AddWithValue("@value", value)
                    Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                End Using
            End Using
        End Function

    End Class

End Namespace
