Imports System.Threading
Imports System.Windows.Forms
Imports Microsoft.VisualStudio.TestTools.UnitTesting

''' <summary>Runs WinForms controllers in tests the way the real application does.</summary>
Public Module UiTestHelper

    ''' <summary>
    ''' Runs <paramref name="test"/> on a UI thread: STA, with a WindowsFormsSynchronizationContext installed up front.
    ''' Without that context, an Await in a controller may resume on a thread-pool thread (the application gets it
    ''' from Application.Run; a test thread has to install it itself).
    ''' </summary>
    Public Sub RunOnUiThread(test As Action)
        Dim failure As Exception = Nothing
        Dim thread As New Thread(Sub()
                                     WindowsFormsSynchronizationContext.AutoInstall = False
                                     SynchronizationContext.SetSynchronizationContext(New WindowsFormsSynchronizationContext())
                                     Try
                                         test()
                                     Catch ex As Exception
                                         failure = ex
                                     End Try
                                 End Sub)
        thread.SetApartmentState(ApartmentState.STA)
        thread.Start()
        thread.Join()
        If failure IsNot Nothing Then Throw New AssertFailedException(failure.ToString(), failure)
    End Sub

    ''' <summary>Pumps messages until <paramref name="condition"/> holds, so async event handlers can finish.</summary>
    Public Sub WaitUntil(condition As Func(Of Boolean), what As String)
        Dim deadline = Date.Now.AddSeconds(10)
        While Not condition()
            If Date.Now > deadline Then Throw New TimeoutException("Timed out waiting for " & what)
            Application.DoEvents()
            Thread.Sleep(20)
        End While
    End Sub

    ''' <summary>Pumps messages for a fixed time, for handlers that give no visible signal when they finish.</summary>
    Public Sub PumpFor(duration As TimeSpan)
        Dim deadline = Date.Now.Add(duration)
        While Date.Now < deadline
            Application.DoEvents()
            Thread.Sleep(20)
        End While
    End Sub

    ''' <summary>A form placed off screen so tests do not flash windows.</summary>
    Public Function OffScreenForm() As Form
        Return New Form With {.ShowInTaskbar = False, .StartPosition = FormStartPosition.Manual, .Location = New Drawing.Point(-3000, -3000)}
    End Function

End Module
