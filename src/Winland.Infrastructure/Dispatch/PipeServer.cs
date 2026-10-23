using System.IO.Pipes;
using Windows.Win32;

namespace Winland.Infrastructure;

/// <summary>
/// The process that answers on a pipe. Install and update find the running Daemon this way, not by
/// the name winland, because the unelevated winland waiting on a UAC relaunch has that name too.
/// </summary>
public static class PipeServer
{
    /// <summary>Null when nothing answers on <paramref name="pipeName"/> within <paramref name="timeoutMs"/>.</summary>
    public static int? ProcessId(string pipeName, int timeoutMs = 2000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        try
        {
            pipe.Connect(timeoutMs);
        }
        catch (TimeoutException)
        {
            return null;
        }

        if (!PInvoke.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id))
            throw new System.ComponentModel.Win32Exception();

        return (int)id;
    }
}
