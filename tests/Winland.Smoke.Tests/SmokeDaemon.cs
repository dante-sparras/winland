using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Winland.Smoke.Tests;

internal readonly record struct SmokePaths(string Daemon, string Control)
{
    public static SmokePaths Read()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "smoke.paths");
        if (!File.Exists(file))
            throw new InvalidOperationException($"Smoke host paths were not written ({file}).");

        var parts = File.ReadAllText(file).Trim().Split('|');
        if (parts.Length != 2)
            throw new InvalidOperationException($"Smoke host paths are malformed ({File.ReadAllText(file)}).");

        var daemon = parts[0].Trim();
        var control = parts[1].Trim();
        if (!File.Exists(daemon) || !File.Exists(control))
            throw new InvalidOperationException($"Smoke hosts are missing ({daemon}, {control}).");

        return new SmokePaths(daemon, control);
    }
}

internal sealed class SmokeDaemon : IDisposable
{
    private readonly object _gate = new();
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _error = new();
    private readonly Process _process;
    private readonly string _directory;
    private readonly string _config;
    private readonly string _dotnetRoot;
    private readonly SmokePaths _paths;
    private bool _disposed;

    private SmokeDaemon(Process process, SmokePaths paths, string directory, string config, string pipe, string state, string dotnetRoot)
    {
        _process = process;
        _paths = paths;
        _directory = directory;
        _config = config;
        _dotnetRoot = dotnetRoot;
        Pipe = pipe;
        State = state;
    }

    public string Pipe { get; }

    public string State { get; }

    public int Id => _process.Id;

    public bool Exited
    {
        get
        {
            _process.Refresh();
            return _process.HasExited;
        }
    }

    public static SmokeDaemon Start(SmokePaths paths, string dotnetRoot)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winland-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var config = Path.Combine(directory, "winland.conf");
        File.WriteAllText(config, ConfigText());
        var pipe = "winland-smoke-" + Guid.NewGuid().ToString("N");
        var state = Path.Combine(directory, "state.json");
        return Launch(paths, directory, config, pipe, state, dotnetRoot);
    }

    public void WaitUntilReady()
    {
        var deadline = Environment.TickCount64 + 20_000;
        string? last = null;
        while (Environment.TickCount64 < deadline)
        {
            if (Exited)
                throw new InvalidOperationException($"winland exited {_process.ExitCode}. {Logs()}");

            if (TryRun("clients", TimeSpan.FromSeconds(2), out _, out var error))
            {
                // The pipe accepts commands before the message loop blocks. Window events
                // are delivered on that loop, so give it a moment to enter the pump.
                Thread.Sleep(300);
                return;
            }

            last = error;
            Thread.Sleep(100);
        }

        throw new TimeoutException($"winland did not accept commands. {last} {Logs()}");
    }

    public string Clients() => Run("clients");

    public string Workspaces() => Run("workspaces");

    public string Monitors() => Run("monitors");

    public string Dispatch(string arguments) => Run("dispatch " + arguments);

    public bool TryStopClean()
    {
        if (Exited)
            return true;

        uint thread;
        try
        {
            thread = MainThread();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return false;
        }

        var deadline = Environment.TickCount64 + 8_000;
        while (Environment.TickCount64 < deadline)
        {
            if (Exited)
                return true;

            // WM_QUIT is the message the daemon posts for a console cancel. GetMessage
            // returns and the process shows hidden windows and restores the taskbars.
            _ = NativeQuit.Post(thread);
            if (_process.WaitForExit(200))
                return true;
        }

        return Exited;
    }

    public void Kill()
    {
        if (Exited)
            return;

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }

        _process.WaitForExit(5_000);
    }

    public void Recover()
    {
        if (!Exited)
            Kill();

        var pipe = Pipe + "-recover";
        using var again = Launch(_paths, _directory, _config, pipe, State, _dotnetRoot);
        again.WaitUntilReady();
        if (!again.TryStopClean())
        {
            again.Kill();
            throw new InvalidOperationException("The recovery run did not stop cleanly. " + again.Logs());
        }
    }

    public string Logs()
    {
        lock (_gate)
            return "stdout: " + Tail(_output) + " stderr: " + Tail(_error);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!Exited)
        {
            if (!TryStopClean())
                Kill();
        }

        _process.Dispose();
    }

    public static void DeleteTree(string directory)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    public static string ResolveDotnetRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (HasRuntime(configured))
            return configured!;

        foreach (var executable in DotnetExecutables())
        {
            var directory = Path.GetDirectoryName(executable);
            if (HasRuntime(directory))
                return directory!;
        }

        foreach (var executable in DotnetExecutables())
        {
            var root = RootFromListRuntimes(executable);
            if (root is not null)
                return root;
        }

        throw new InvalidOperationException("Could not find a .NET install to set DOTNET_ROOT.");
    }

    private static SmokeDaemon Launch(SmokePaths paths, string directory, string config, string pipe, string state, string dotnetRoot)
    {
        var info = new ProcessStartInfo(paths.Daemon)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = directory,
        };
        info.ArgumentList.Add("--config");
        info.ArgumentList.Add(config);
        info.Environment["WINLAND_PIPE"] = pipe;
        info.Environment["WINLAND_STATE"] = state;
        info.Environment["DOTNET_ROOT"] = dotnetRoot;
        // A hand start relaunches elevated. WINLAND_NO_ELEVATE=1 exists for tests.
        // Elevated features then do not work.
        info.Environment["WINLAND_NO_ELEVATE"] = "1";

        var process = new Process { StartInfo = info };
        var daemon = new SmokeDaemon(process, paths, directory, config, pipe, state, dotnetRoot);
        process.OutputDataReceived += (_, args) => daemon.Append(daemon._output, args.Data);
        process.ErrorDataReceived += (_, args) => daemon.Append(daemon._error, args.Data);
        if (!process.Start())
            throw new InvalidOperationException("winland did not start.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return daemon;
    }

    private string Run(string arguments)
    {
        if (!TryRun(arguments, TimeSpan.FromSeconds(10), out var reply, out var error))
            throw new InvalidOperationException($"winlandctl {arguments} failed. {error} {Logs()}");

        return reply.Trim();
    }

    private bool TryRun(string arguments, TimeSpan timeout, out string reply, out string error)
    {
        reply = "";
        error = "";
        var info = new ProcessStartInfo(_paths.Control)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _directory,
        };
        foreach (var part in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            info.ArgumentList.Add(part);

        info.Environment["WINLAND_PIPE"] = Pipe;
        info.Environment["DOTNET_ROOT"] = _dotnetRoot;

        using var process = new Process { StartInfo = info };
        if (!process.Start())
        {
            error = "winlandctl did not start.";
            return false;
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
            }

            error = $"winlandctl {arguments} timed out.";
            return false;
        }

        reply = stdout.GetAwaiter().GetResult();
        error = stderr.GetAwaiter().GetResult();
        return process.ExitCode == 0;
    }

    private void Append(StringBuilder builder, string? line)
    {
        if (line is null)
            return;

        lock (_gate)
            builder.AppendLine(line);
    }

    private uint MainThread()
    {
        _process.Refresh();
        ProcessThread? main = null;
        foreach (ProcessThread thread in _process.Threads)
        {
            try
            {
                if (main is null || thread.StartTime < main.StartTime)
                    main = thread;
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
            }
        }

        if (main is null)
            throw new InvalidOperationException("winland has no thread to stop.");

        return (uint)main.Id;
    }

    private static string Tail(StringBuilder builder)
    {
        var text = builder.ToString();
        return text.Length <= 2000 ? text : text[^2000..];
    }

    private static string ConfigText() =>
        """
        general:gaps_in = 0
        general:gaps_out = 0
        general:border_size = 0
        dwindle:split_width_multiplier = 0.1
        windowrulev2 = ignore, class:.*
        windowrulev2 = tile, class:^(WinlandSmokeWindow)$, title:^(WinlandSmoke [12])$
        windowrulev2 = workspace 1, class:^(WinlandSmokeWindow)$, title:^(WinlandSmoke [12])$
        """;

    private static bool HasRuntime(string? root) =>
        !string.IsNullOrWhiteSpace(root)
        && Directory.Exists(Path.Combine(root, "shared", "Microsoft.NETCore.App"));

    private static IEnumerable<string> DotnetExecutables()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            yield break;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory.Trim(), "dotnet.exe");
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(candidate))
                yield return candidate;
        }
    }

    private static string? RootFromListRuntimes(string executable)
    {
        var info = new ProcessStartInfo(executable, "--list-runtimes")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(info);
        if (process is null)
            return null;

        var text = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            return null;
        }

        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains("Microsoft.NETCore.App", StringComparison.Ordinal))
                continue;

            var start = line.IndexOf('[', StringComparison.Ordinal);
            var end = line.IndexOf(']', StringComparison.Ordinal);
            if (start < 0 || end <= start)
                continue;

            var shared = line[(start + 1)..end].Trim();
            var root = Path.GetFullPath(Path.Combine(shared, "..", ".."));
            if (HasRuntime(root))
                return root;
        }

        return null;
    }
}

internal static class NativeQuit
{
    public static bool Post(uint threadId) =>
        Windows.Win32.PInvoke.PostThreadMessage(threadId, 0x0012, default, default);
}

internal static class UserState
{
    public static string Path() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Winland",
            "state.json");

    public static DateTime? Stamp()
    {
        var path = Path();
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }
}

internal static class Taskbars
{
    public static bool AllVisible(out string detail)
    {
        var primary = Find("Shell_TrayWnd", IntPtr.Zero, child: false);
        if (primary == IntPtr.Zero)
        {
            detail = "Shell_TrayWnd was not found.";
            return false;
        }

        if (!Windows.Win32.PInvoke.IsWindowVisible(new Windows.Win32.Foundation.HWND(primary)))
        {
            detail = "The primary taskbar is hidden.";
            return false;
        }

        var after = IntPtr.Zero;
        while (true)
        {
            var next = Find("Shell_SecondaryTrayWnd", after, child: true);
            if (next == IntPtr.Zero)
                break;

            if (!Windows.Win32.PInvoke.IsWindowVisible(new Windows.Win32.Foundation.HWND(next)))
            {
                detail = "A secondary taskbar is hidden.";
                return false;
            }

            after = next;
        }

        detail = "Taskbars are visible.";
        return true;
    }

    private static unsafe IntPtr Find(string className, IntPtr after, bool child)
    {
        fixed (char* name = className)
        {
            var found = child
                ? Windows.Win32.PInvoke.FindWindowEx(default, new Windows.Win32.Foundation.HWND(after), name, null)
                : Windows.Win32.PInvoke.FindWindow(name, null);
            return (IntPtr)found;
        }
    }
}

internal sealed class EventLog : IDisposable
{
    private readonly Process _process;
    private readonly List<string> _lines = [];
    private readonly object _gate = new();
    private readonly StringBuilder _error = new();

    private EventLog(Process process) => _process = process;

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
                return [.. _lines];
        }
    }

    public static EventLog Follow(SmokePaths paths, string pipe, string dotnetRoot)
    {
        var info = new ProcessStartInfo(paths.Control, "events")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["WINLAND_PIPE"] = pipe;
        info.Environment["DOTNET_ROOT"] = dotnetRoot;
        var process = new Process { StartInfo = info };
        var log = new EventLog(process);
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
                return;

            lock (log._gate)
                log._lines.Add(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                log._error.AppendLine(args.Data);
        };
        if (!process.Start())
            throw new InvalidOperationException("winlandctl events did not start.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Thread.Sleep(750);
        if (process.HasExited)
            throw new InvalidOperationException($"winlandctl events exited {process.ExitCode}. {log._error}");

        return log;
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
            }

            _process.WaitForExit(2_000);
        }

        _process.Dispose();
    }
}

internal static class Addresses
{
    public static string Of(Windows.Win32.Foundation.HWND hwnd) =>
        "0x" + ((IntPtr)hwnd).ToInt64().ToString("x", CultureInfo.InvariantCulture);
}
