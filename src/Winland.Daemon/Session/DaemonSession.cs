using System.Diagnostics;
using Winland.Install;

namespace Winland.Daemon;

/// <summary>Asks the running Daemon to exit, then starts the installed one.</summary>
sealed class DaemonSession : IDaemonSession
{
    public void Stop()
    {
        var pipe = Pipe();
        if (PipeServer.ProcessId(pipe ?? CommandProtocol.PipeName) is not int id)
            return;

        Process daemon;
        try
        {
            daemon = Process.GetProcessById(id);
        }
        catch (ArgumentException)
        {
            return;
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
    }

    public void Start(string executable, IReadOnlyList<string> arguments)
    {
        // start detaches the Daemon from this console, so it keeps running after install exits.
        // The parent is already elevated for a machine install, and cmd inherits that token,
        // so this does not ask for a second approval. A per-user install passes --no-elevate.
        var command = Quote(executable);
        if (arguments.Count > 0)
            command += " " + string.Join(' ', arguments.Select(Quote));

        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = "/d /c start \"Winland\" " + command,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Winland did not start.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Winland did not start.");
    }

    private static string Quote(string value)
    {
        if (value.Contains('"'))
            throw new ArgumentException($"Cannot quote '{value}': it contains a quote.", nameof(value));

        return "\"" + value + "\"";
    }

    private static string? Pipe()
    {
        var configured = Environment.GetEnvironmentVariable("WINLAND_PIPE");
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }
}
