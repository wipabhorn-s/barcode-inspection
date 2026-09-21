Option Strict On

Imports System.Configuration
Imports System.Threading
Imports Npgsql

Namespace Data

    ''' <summary>Thrown when the database cannot be reached after all retries.</summary>
    Public Class DatabaseUnavailableException
        Inherits Exception

        Public Sub New(message As String, innerException As Exception)
            MyBase.New(message, innerException)
        End Sub
    End Class

    ''' <summary>
    ''' Opens PostgreSQL connections with retry, and reports whether the database is reachable.
    ''' The connection string comes from configuration, never from source code.
    ''' </summary>
    Public Class DbConnectionFactory

        ''' <summary>Name of the entry in connectionStrings.config.</summary>
        Public Const ConnectionStringName As String = "QaDatabase"

        ''' <summary>Environment variable that overrides the config file (useful on test machines).</summary>
        Public Const ConnectionStringEnvironmentVariable As String = "BARCODE_INSPECTION_DB"

        Private Const InitialRetryDelayMs As Integer = 500

        Private ReadOnly _connectionString As String

        ''' <summary>Raised after every connection attempt. May be raised on a background thread.</summary>
        Public Event ConnectionStatusChanged(isConnected As Boolean)

        Public Sub New(connectionString As String)
            If String.IsNullOrWhiteSpace(connectionString) Then Throw New ArgumentException("Connection string is empty.", NameOf(connectionString))
            _connectionString = connectionString
        End Sub

        ''' <summary>Creates a factory from the environment variable, or from connectionStrings.config.</summary>
        Public Shared Function FromConfiguration() As DbConnectionFactory
            Dim fromEnvironment = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            If Not String.IsNullOrWhiteSpace(fromEnvironment) Then Return New DbConnectionFactory(fromEnvironment)

            Dim setting = ConfigurationManager.ConnectionStrings(ConnectionStringName)
            If setting Is Nothing OrElse String.IsNullOrWhiteSpace(setting.ConnectionString) Then
                Throw New ConfigurationErrorsException(
                    $"Connection string '{ConnectionStringName}' was not found. " &
                    "Copy connectionStrings.example.config to connectionStrings.config and fill in the database details.")
            End If

            Return New DbConnectionFactory(setting.ConnectionString)
        End Function

        ''' <summary>
        ''' Opens a connection, retrying with exponential back-off. Returns Nothing when the database is unreachable.
        ''' Kept synchronous for existing callers; new code should prefer <see cref="OpenConnectionAsync"/>.
        ''' </summary>
        Public Function TryOpenConnection(Optional maxRetries As Integer = 3) As NpgsqlConnection
            Dim delayMs As Integer = InitialRetryDelayMs

            For attempt As Integer = 1 To maxRetries
                Try
                    Dim conn = OpenAndPing()
                    RaiseEvent ConnectionStatusChanged(True)
                    Return conn
                Catch ex As NpgsqlException When attempt < maxRetries
                    Debug.WriteLine($"Connection attempt {attempt}/{maxRetries} failed: {ex.Message}")
                    Thread.Sleep(delayMs)
                    delayMs *= 2
                Catch ex As Exception
                    Debug.WriteLine($"Connection failed after {attempt} attempt(s): {ex.Message}")
                    Exit For
                End Try
            Next

            RaiseEvent ConnectionStatusChanged(False)
            Return Nothing
        End Function

        ''' <summary>Opens a connection without blocking the calling thread. Throws when the database is unreachable.</summary>
        Public Async Function OpenConnectionAsync(Optional maxRetries As Integer = 3, Optional cancellationToken As CancellationToken = Nothing) As Task(Of NpgsqlConnection)
            Dim delayMs As Integer = InitialRetryDelayMs
            Dim lastError As Exception = Nothing

            For attempt As Integer = 1 To maxRetries
                Dim conn As New NpgsqlConnection(_connectionString)
                Try
                    Await conn.OpenAsync(cancellationToken).ConfigureAwait(False)
                    RaiseEvent ConnectionStatusChanged(True)
                    Return conn
                Catch ex As NpgsqlException
                    conn.Dispose()
                    lastError = ex
                    Debug.WriteLine($"Connection attempt {attempt}/{maxRetries} failed: {ex.Message}")
                End Try

                If attempt < maxRetries Then
                    Await Task.Delay(delayMs, cancellationToken).ConfigureAwait(False)
                    delayMs *= 2
                End If
            Next

            RaiseEvent ConnectionStatusChanged(False)
            Throw New DatabaseUnavailableException("Cannot connect to database.", lastError)
        End Function

        Private Function OpenAndPing() As NpgsqlConnection
            Dim conn As New NpgsqlConnection(_connectionString)
            Try
                conn.Open()
                Using cmd As New NpgsqlCommand("SELECT 1", conn)
                    cmd.CommandTimeout = 5
                    cmd.ExecuteScalar()
                End Using
                Return conn
            Catch
                conn.Dispose()
                Throw
            End Try
        End Function

    End Class

End Namespace
