using System.Diagnostics;
using Winland.Install;

namespace Winland;

/// <summary>Asks the running Daemon to exit, then starts the installed one.</summary>
sealed class DaemonSession : IDaemonSession
{
    public void Stop()
    {
        var others = Process.GetProcessesByName("winland")
            .Where(process => process.Id != Environment.ProcessId)
            .ToArray();
        if (others.Length == 0)
            return;

        try
        {
            try
            {
                CommandClient.Transact("dispatch exit", Pipe());
            }
            catch (InvalidOperationException exception) when (exception.Message == "Winland is not running.")
            {
            }

            var deadline = Environment.TickCount64 + 20_000;
            while (Environment.TickCount64 < deadline)
            {
                if (others.All(Exited))
                    return;

                Thread.Sleep(200);
            }

            throw new InvalidOperationException("Winland did not exit.");
        }
        finally
        {
            foreach (var process in others)
                process.Dispose();
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

    private static bool Exited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string? Pipe()
    {
        var configured = Environment.GetEnvironmentVariable("WINLAND_PIPE");
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }
}
