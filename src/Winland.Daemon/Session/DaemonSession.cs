using System.Diagnostics;
using Winland.Install;

namespace Winland.Daemon;

/// <summary>Asks the running Daemon to exit, then starts the installed one.</summary>
sealed class DaemonSession : IDaemonSession
{
    public bool Stop()
    {
        var pipe = Pipe();
        if (PipeServer.ProcessId(pipe) is not int id)
            return false;

        Process daemon;
        try
        {
            daemon = Process.GetProcessById(id);
        }
        catch (ArgumentException)
        {
            return false;
        }

        using (daemon)
        {
            try
            {
                CommandClient.Transact("dispatch exit", pipe);
            }
            catch (InvalidOperationException exception) when (exception.Message == "Winland is not running.")
            {
            }

            if (!daemon.WaitForExit(20_000))
                throw new InvalidOperationException("Winland did not exit.");
        }

        return true;
    }

    public void Start(string executable, IReadOnlyList<string> arguments)
    {
        // The shell starts the Daemon in a console of its own, hidden, so it shows no window and keeps running
        // after install exits. It inherits no handle, so a caller that reads install's output is not held open.
        // The parent is already elevated for a machine install, and the Daemon inherits that token,
        // so this does not ask for a second approval. A per-user install passes --no-elevate.
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Winland did not start.");
    }

    private static string? Pipe()
    {
        var configured = Environment.GetEnvironmentVariable("WINLAND_PIPE");
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }
}
