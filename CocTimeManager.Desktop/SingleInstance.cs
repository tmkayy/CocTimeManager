using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace CocTimeManager.Desktop;

/// <summary>
/// Keeps one copy of the app running (otherwise a login-started copy plus a launcher-started copy
/// would both play the "done" sound). A second launch asks the first to show its window, then exits.
/// </summary>
public static class SingleInstance
{
    private static readonly string PipeName = "CocTimeManager-" + Environment.UserName;

    /// <summary>True if another copy is running and was told to show itself.</summary>
    public static bool SignalExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(300);
            client.WriteByte(1);
            client.Flush();
            return true;
        }
        catch (Exception)
        {
            return false; // nobody listening: we are the first
        }
    }

    /// <summary>Listens for later launches; <paramref name="onShow"/> runs on a background thread.</summary>
    public static void Listen(Action onShow)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync();
                    if (server.ReadByte() == 1) onShow();
                }
                catch (IOException)
                {
                    // Another process owns the pipe name or the client vanished: back off and retry.
                    await Task.Delay(1000);
                }
                catch (Exception)
                {
                    return;
                }
            }
        });
    }
}
