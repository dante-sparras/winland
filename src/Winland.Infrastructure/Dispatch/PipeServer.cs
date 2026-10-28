using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace Winland.Infrastructure;

/// <summary>
/// The process that answers on a pipe. Install and update find the running Daemon this way, not by
/// the name winland, because the unelevated winland waiting on a UAC relaunch has that name too.
/// </summary>
public static class PipeServer
{
    public static string DefaultName
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return CommandProtocol.PipeName + "-" + process.SessionId.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Null when nothing answers on <paramref name="pipeName"/> within <paramref name="timeoutMs"/>.</summary>
    public static int? ProcessId(string? pipeName = null, int timeoutMs = 2000)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName ?? DefaultName, PipeDirection.InOut);
        try
        {
            pipe.Connect(timeoutMs);
        }
        catch (TimeoutException)
        {
            return null;
        }

        return VerifyServer(pipe);
    }

    // Check before sending a command or lending foreground rights, including when a different pipe was requested.
    internal static int VerifyServer(NamedPipeClientStream pipe)
    {
        if (!PInvoke.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id))
            throw new Win32Exception();

        using var server = Process.GetProcessById((int)id);
        using var current = Process.GetCurrentProcess();
        if (server.SessionId != current.SessionId)
            throw new InvalidOperationException("The command pipe belongs to another session.");

        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, id);
        if (process.IsInvalid || !PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
            throw new Win32Exception();

        using (token)
        using (var owner = new WindowsIdentity(token.DangerousGetHandle()))
        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (owner.User != identity.User)
                throw new InvalidOperationException("The command pipe belongs to another user.");
        }

        return (int)id;
    }
}
