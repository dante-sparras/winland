using System.Diagnostics;
using Winland.Core;
using Winland.Core.Diagnostics;
using Winland.Platform.Win32;

namespace Winland;

/// <summary>
/// Starts Winland again when the process dies without a clean shutdown, and puts back the windows a dead window manager left hidden.
/// The word in <c>lifetime</c> says which (<see cref="Lifetime.AfterExit"/>): one that was replaced is waited for, and nothing is shown meanwhile.
/// The shell's watcher does not touch those windows. Three restarts inside a minute stop the chain so a broken startup cannot loop.
/// </summary>
internal static class WinlandWatch
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int RestartLimit = 3;
    private const string WatchName = "Winland.Watch.exe";
    private const int ArrivalPollMs = 250;

    private static string LifetimePath => Path.Combine(ConfigFolder.Path, Lifetime.FileName);

    private static string RestartPath => Path.Combine(ConfigFolder.Path, "winland-restarts");

    /// <summary>True when this process is the child waiting on a window manager.</summary>
    public static bool IsWatch(string[] args) => args.Length >= 2 && args[0] == "--watch";

    /// <summary>
    /// Starts the child that will relaunch this process. The child runs from a copy outside the live folder,
    /// so a swap can overwrite that folder while the child waits. A failure here leaves this process running without a watcher.
    /// </summary>
    public static void Arm()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                Log.Warning("Winland", "no process path; the window-manager watcher did not start");
                return;
            }

            var home = Path.GetDirectoryName(exe) ?? "";
            var staged = Stage(home);
            if (staged is not null && !HasRuntime(staged) && HasRuntime(home))
            {
                // A copy whose hostfxr.dll was removed cannot start. The WinExe then waits on a hidden dialog and never sees the lock.
                Log.Warning("Winland", "the watcher copy has no hostfxr.dll; it will run from the live folder");
                staged = null;
            }

            var launched = staged is null ? exe : Path.Combine(staged, Path.GetFileName(exe));
            var start = new ProcessStartInfo(WatchPath(launched))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("--watch");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(exe);
            Process.Start(start)?.Dispose();
            Log.Information("Winland", "watching process " + Environment.ProcessId + (staged is null ? "" : " from " + staged));
        }
        catch (Exception ex)
        {
            Log.Error("Winland", "could not arm the window-manager watcher", ex);
        }
    }

    /// <summary>
    /// Blocks until <paramref name="args"/>'s process id exits, then does what the word in <c>lifetime</c> asks.
    /// After a crash the windows Winland hid are shown at once and Winland is started again. After a Stop they are shown and Winland stays down.
    /// One that was replaced is waited for, and its windows are shown only when no window manager comes. The taskbar is the shell's.
    /// </summary>
    /// <param name="args">The watch switch, the window manager's process id, and its path.</param>
    /// <param name="flush">Writes the log lines queued so far.</param>
    public static void Watch(string[] args, Action flush)
    {
        int? dead = null;
        if (!int.TryParse(args[1], out var pid))
        {
            Log.Warning("Winland", "watch argument was not a process id");
        }
        else
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                var started = StartedAt(process);
                Log.Information("Winland", "waiting for process " + pid);
                try
                {
                    flush();
                }
                catch (Exception ex)
                {
                    Log.Warning("Winland", "could not write the log before the wait", ex);
                }

                // Ask until the id is gone. WaitForExit on this STA thread does not return when the process has already gone.
                while (!WatchedProcess.Gone(started, Look(pid, started)))
                {
                    Thread.Sleep(ArrivalPollMs);
                }

                Log.Information("Winland", "process " + pid + " exited");
                dead = pid;
            }
            catch (ArgumentException)
            {
                Log.Information("Winland", "watched process " + pid + " had already exited");
                dead = pid;
            }
            catch (Exception ex)
            {
                Log.Error("Winland", "could not wait for process " + pid, ex);
            }
        }

        var word = Environment.HasShutdownStarted ? LifetimeWord.Stopped : ReadLifetime();
        var since = Stopwatch.StartNew();
        var step = Lifetime.AfterExit(word, Arrived(), since.Elapsed);
        if (step == ExitStep.Wait)
        {
            Log.Information(
                "Winland",
                "process " + pid + " was replaced; waiting up to " + (int)Lifetime.ReplaceLimit.TotalSeconds + " seconds for the next window manager, and showing nothing until then");
            do
            {
                Thread.Sleep(ArrivalPollMs);
                step = Lifetime.AfterExit(word, Arrived(), since.Elapsed);
            }
            while (step == ExitStep.Wait);
        }

        if (step == ExitStep.Leave)
        {
            Log.Information("Winland", "another window manager holds Local\\Winland after " + since.ElapsedMilliseconds + " ms; leaving it what process " + pid + " left");
            return;
        }

        WindowFade.RestoreLeft();
        if (dead is int desktop)
        {
            HiddenStore.Rescue(desktop);
        }
        else
        {
            HiddenStore.Rescue();
        }

        if (step == ExitStep.ShowAndStayDown)
        {
            if (word == LifetimeWord.Replacing)
            {
                GiveCaptionsBack();
                Log.Warning("Winland", "no window manager came in " + (int)Lifetime.ReplaceLimit.TotalSeconds + " seconds to replace process " + pid + "; leaving Winland down");
                return;
            }

            GiveCaptionsBack();
            Log.Information("Winland", "leaving Winland down");
            return;
        }

        if (!BudgetAllows())
        {
            GiveCaptionsBack();
            Log.Warning("Winland", "restart skipped after 3 crashes in a minute");
            return;
        }

        NoteRestart();
        var exe = args.Length > 2 ? args[2] : null;
        if (string.IsNullOrWhiteSpace(exe))
        {
            GiveCaptionsBack();
            Log.Warning("Winland", "no window-manager path; did not restart");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })?.Dispose();
            Log.Warning("Winland", "restarted after a crash");
        }
        catch (Exception ex)
        {
            GiveCaptionsBack();
            Log.Error("Winland", "could not restart the window manager", ex);
        }
    }

    private static bool HasRuntime(string folder) => File.Exists(Path.Combine(folder, "hostfxr.dll"));

    private static DateTimeOffset? StartedAt(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static WatchedProcess.Sight? Look(int pid, DateTimeOffset? started)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            try
            {
                if (process.HasExited)
                {
                    return new WatchedProcess.Sight(started ?? default, true);
                }
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            try
            {
                return new WatchedProcess.Sight(new DateTimeOffset(process.StartTime), false);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The id is live and its start could not be read. Reuse cannot be told apart, so this is still the one.
                return new WatchedProcess.Sight(started ?? default, false);
            }
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool ExeInUse(string folder)
    {
        var exe = Path.Combine(folder, WatchName);
        if (!File.Exists(exe))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool Arrived()
    {
        try
        {
            if (!Mutex.TryOpenExisting(@"Local\Winland", out var held))
            {
                return false;
            }

            held.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void GiveCaptionsBack()
    {
        try
        {
            WindowGateway.RestoreCaptionsWithoutDesktop();
        }
        catch (Exception ex)
        {
            Log.Warning("Winland", "could not give the caption buttons back", ex);
        }
    }

    private static LifetimeWord ReadLifetime()
    {
        if (!Lifetime.TryRead(LifetimePath, out var word, out var error))
        {
            Log.Warning("Winland", "could not read lifetime, so the window manager that wrote it is taken to have crashed", error);
        }

        return word;
    }

    private static string WatchPath(string exe)
    {
        var watch = Path.Combine(Path.GetDirectoryName(exe) ?? "", WatchName);
        return File.Exists(watch) ? watch : exe;
    }

    private static string? Stage(string home)
    {
        if (home.Length == 0 || !Directory.Exists(home))
        {
            return null;
        }

        var root = Path.Combine(home, "watch");
        var stamp = "w-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var dest = Path.Combine(root, stamp);
        try
        {
            Directory.CreateDirectory(dest);
            var copy = new ProcessStartInfo("robocopy")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            copy.ArgumentList.Add(home);
            copy.ArgumentList.Add(dest);
            copy.ArgumentList.Add("/E");
            copy.ArgumentList.Add("/XD");
            copy.ArgumentList.Add(root);
            copy.ArgumentList.Add("/XF");
            copy.ArgumentList.Add("*.old");
            copy.ArgumentList.Add("/R:1");
            copy.ArgumentList.Add("/W:1");
            copy.ArgumentList.Add("/NFL");
            copy.ArgumentList.Add("/NDL");
            copy.ArgumentList.Add("/NJH");
            copy.ArgumentList.Add("/NJS");
            copy.ArgumentList.Add("/NP");
            using var process = Process.Start(copy);
            if (process is null || !process.WaitForExit(60_000))
            {
                try
                {
                    process?.Kill(entireProcessTree: true);
                }
                catch (Exception kill) when (kill is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }

                Log.Warning("Winland", "the watcher copy did not finish; it will run from the live folder");
                return null;
            }

            if (process.ExitCode >= 8)
            {
                Log.Warning("Winland", "the watcher copy failed with " + process.ExitCode + "; it will run from the live folder");
                return null;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Winland", "the watcher copy did not start; it will run from the live folder", ex);
            return null;
        }

        try
        {
            foreach (var old in Directory.GetDirectories(root))
            {
                if (!WatchCopies.Removable(old, dest, winland: true))
                {
                    continue;
                }

                // Directory.Delete removes every file the process has not locked, then fails on the executable.
                // That leaves a watcher with no hostfxr.dll, and the WinExe waits on a hidden dialog.
                if (ExeInUse(old))
                {
                    Log.Information("Winland", "left watcher copy " + Path.GetFileName(old) + "; its executable is still running");
                    continue;
                }

                try
                {
                    Directory.Delete(old, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning("Winland", "an old watcher copy was left in place", ex);
        }

        return dest;
    }

    private static bool BudgetAllows() => RecentRestarts().Count < RestartLimit;

    private static void NoteRestart()
    {
        var recent = RecentRestarts();
        recent.Add(DateTimeOffset.Now);
        try
        {
            Directory.CreateDirectory(ConfigFolder.Path);
            File.WriteAllLines(RestartPath, recent.Select(stamp => stamp.ToString("o")));
        }
        catch (Exception ex)
        {
            Log.Warning("Winland", "could not record the restart", ex);
        }
    }

    private static List<DateTimeOffset> RecentRestarts()
    {
        var cutoff = DateTimeOffset.Now - Window;
        try
        {
            if (!File.Exists(RestartPath))
            {
                return [];
            }

            return File.ReadAllLines(RestartPath)
                .Select(line => DateTimeOffset.TryParse(line, out var stamp) ? stamp : (DateTimeOffset?)null)
                .Where(stamp => stamp is not null && stamp > cutoff)
                .Select(stamp => stamp!.Value)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning("Winland", "could not read restarts", ex);
            return [];
        }
    }
}
