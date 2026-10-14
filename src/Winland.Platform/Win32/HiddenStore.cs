
namespace Winland.Platform.Win32;

/// <summary>
/// Keeps the desktop's record of the windows it has hidden (<see cref="HiddenWindows"/>) in the user folder,
/// and puts back the windows a desktop that was killed left hidden.
/// </summary>
public static class HiddenStore
{
    private static readonly object Gate = new();

    // The windows last written, each with the way the record gives it. The desktop's thread writes,
    // and so does whichever thread hides a window another way than the record says.
    private static readonly Dictionary<long, HideWay> Written = [];

    // What the file holds: the windows of the last write that landed, each with its way. A write that failed leaves the file,
    // and this, as they were.
    private static readonly Dictionary<long, HideWay> OnFile = [];

    private static string FilePath => Path.Combine(ConfigFolder.Path, HiddenWindows.FileName);

    /// <summary>
    /// Writes the windows this desktop has hidden, each with how (<see cref="HiddenLedger"/>). A window not hidden yet is written as cloaked,
    /// the way every window is tried first. No hidden window is no file.
    /// The new record replaces the old one in one move, so a kill in the middle leaves the old record whole.
    /// </summary>
    /// <param name="ids">Every window that is hidden now or about to be.</param>
    internal static void Write(IReadOnlyCollection<long> ids)
    {
        lock (Gate)
        {
            Written.Clear();
            foreach (var id in ids)
            {
                Written[id] = HideWay.Cloak;
            }

            Write();
        }
    }

    /// <summary>
    /// Makes the file name <paramref name="id"/> with <paramref name="way"/> before the window is hidden so (<see cref="IHideHost.Record"/>).
    /// It writes when the file does not: the shell never named the window, the last write failed, or the way is another than the one written,
    /// <c>SW_HIDE</c> for a window the cloak was not used on, or the cloak for a window that was hidden with <c>SW_HIDE</c> before.
    /// A rescue undoes only the way the record names, so a desktop killed after the hide would otherwise leave the window hidden for good.
    /// </summary>
    /// <param name="id">The window. The ledger already holds <paramref name="way"/> for it.</param>
    /// <param name="way">The way the window is about to be hidden.</param>
    /// <returns>False when the file still does not name the window with that way. <see cref="WindowHide"/> then leaves it on the screen.</returns>
    internal static bool Record(long id, HideWay way)
    {
        lock (Gate)
        {
            if (!OnFile.TryGetValue(id, out var filed) || filed != way)
            {
                Written[id] = way;
                Write();
            }

            return OnFile.TryGetValue(id, out filed) && filed == way;
        }
    }

    private static void Write()
    {
        try
        {
            var windows = new List<HiddenWindow>(Written.Count);
            foreach (var id in Written.Keys.ToArray())
            {
                var way = HiddenLedger.Windows.Way(id) ?? HideWay.Cloak;
                Written[id] = way;
                if (Owner((nint)id) is var (process, started))
                {
                    windows.Add(new HiddenWindow(id, process, started, way));
                }
            }

            if (windows.Count == 0)
            {
                File.Delete(FilePath);
                OnFile.Clear();
                return;
            }

            Directory.CreateDirectory(ConfigFolder.Path);
            var next = FilePath + ".new";
            File.WriteAllText(next, new HiddenWindows(Environment.ProcessId, windows).Text());
            File.Move(next, FilePath, overwrite: true);
            OnFile.Clear();
            foreach (var window in windows)
            {
                OnFile[window.Id] = window.Way;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning("Shell", "could not record the hidden windows", ex);
        }
    }

    // The windows a replaced desktop left hidden, taken over and not yet handed to the shell.
    private static readonly List<long> Left = [];

    /// <summary>
    /// What a desktop that starts does with the record it finds, once it is the only desktop. It is a dead desktop's record, and its watcher may be gone too.
    /// After a crash or a Stop every window on it is put back at once (<see cref="Rescue"/>). A desktop that was replaced left them for this one
    /// (<see cref="Lifetime.TakesOverHidden"/>): none is shown, and the shell shows the ones it will not keep hidden (<see cref="TakeLeft"/>).
    /// </summary>
    /// <param name="found">The word <c>lifetime</c> held when this desktop started.</param>
    public static void Inherit(LifetimeWord found)
    {
        if (Lifetime.TakesOverHidden(found))
        {
            TakeOver();
        }
        else
        {
            Rescue();
        }
    }

    /// <summary>
    /// Makes the windows the record names this desktop's own, as hidden as they are: each goes on the ledger with the way the record gives it,
    /// and the record is written again under this desktop. From then on this desktop's watcher shows them if it dies, and the old desktop's
    /// watcher, which takes only its own desktop's record, leaves them. Until the write lands the record is the old desktop's, and whole.
    /// </summary>
    private static void TakeOver()
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }

                var record = HiddenWindows.Read(File.ReadAllText(FilePath));
                Written.Clear();
                Left.Clear();
                foreach (var window in record.Stranded(Read))
                {
                    HiddenLedger.Windows.Adopt(window.Id, window.Way);
                    Written[window.Id] = window.Way;
                    Left.Add(window.Id);
                }

                Write();
                Log.Information(
                    "Shell",
                    "took over " + Left.Count + " of " + record.Windows.Count + " windows desktop " + record.Desktop + " left hidden; none is shown yet");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning("Shell", "could not read the hidden windows", ex);
        }
    }

    /// <summary>The windows <see cref="Inherit"/> took over from a replaced desktop, still hidden. The shell asks once, as it first places windows.</summary>
    /// <returns>Each window, in the order the record had them. Empty the second time, and when nothing was taken over.</returns>
    internal static IReadOnlyList<long> TakeLeft()
    {
        lock (Gate)
        {
            if (Left.Count == 0)
            {
                return [];
            }

            long[] left = [.. Left];
            Left.Clear();
            return left;
        }
    }

    /// <summary>
    /// Puts back the windows a dead desktop left hidden, each the way the record says it was hidden, and removes its record.
    /// A desktop that starts after a crash or a Stop takes any record (<see cref="Inherit"/>). The desktop watcher takes only the record its own desktop wrote,
    /// because a newer desktop may have taken the record over, or be hiding windows of its own (<see cref="HiddenWindows.WrittenBy"/>).
    /// The record stays when Explorer did not take a cloak off, so the next reader asks again.
    /// </summary>
    /// <param name="desktop">Process id of the desktop that died. Null takes any record.</param>
    public static void Rescue(int? desktop = null)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            var record = HiddenWindows.Read(File.ReadAllText(FilePath));
            if (!record.WrittenBy(desktop))
            {
                return;
            }

            var stranded = record.Stranded(Read);
            var shown = 0;
            var waited = 0;
            foreach (var window in stranded)
            {
                if (PutBack(window, ref waited))
                {
                    shown++;
                }
            }

            if (shown == stranded.Count)
            {
                File.Delete(FilePath);
            }

            Log.Information(
                "Shell",
                "showed " + shown + " of " + record.Windows.Count + " windows desktop " + record.Desktop + " left hidden"
                + (shown == stranded.Count ? "" : "; " + (stranded.Count - shown) + " are still cloaked"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning("Shell", "could not read the hidden windows", ex);
        }
    }

    /// <summary>Undoes one hide. False when the window is still cloaked.</summary>
    /// <param name="window">The window the record left hidden.</param>
    /// <param name="waited">How many times this rescue has waited for Explorer, for any window (<see cref="HiddenWindows.RescueWait"/>).</param>
    private static bool PutBack(HiddenWindow window, ref int waited)
    {
        var hwnd = (nint)window.Id;
        if (window.Way == HideWay.Hide)
        {
            HiddenLedger.Show(hwnd, Native.SwShowNa);
            return true;
        }

        // The desktop may have died with Explorer, and Explorer takes a moment to come back. The cloak outlives both.
        while (true)
        {
            var refusal = ShellCloak.Set(hwnd, cloak: false);
            if (refusal == CloakRefusal.None)
            {
                return true;
            }

            if (HiddenWindows.RescueWait(refusal, waited) is not TimeSpan wait)
            {
                return false;
            }

            waited++;
            Thread.Sleep(wait);
        }
    }

    private static HiddenReading? Read(long id)
    {
        var hwnd = (nint)id;
        if (Owner(hwnd) is not var (process, started))
        {
            return null;
        }

        return new HiddenReading(process, started, Native.IsWindowVisible(hwnd), Native.ShellCloaked(hwnd));
    }

    /// <summary>The process that owns the window now, and when it started. Null when the handle names no window.</summary>
    private static (int Process, long Started)? Owner(nint hwnd)
    {
        if (hwnd == 0 || !Native.IsWindow(hwnd))
        {
            return null;
        }

        _ = Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        long started = 0;
        var process = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, pid);
        if (process != 0)
        {
            try
            {
                if (!Native.GetProcessTimes(process, out started, out _, out _, out _))
                {
                    started = 0;
                }
            }
            finally
            {
                Native.CloseHandle(process);
            }
        }

        return ((int)pid, started);
    }
}
