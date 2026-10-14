using System.Globalization;
using System.Text;

namespace Winland.Core;

/// <summary>
/// A window Winoma hid, the process that owned it then, and how it was hidden.
/// Windows hands a closed window's handle to a later one, so the handle alone does not name the window.
/// </summary>
/// <param name="Id">Native handle.</param>
/// <param name="Process">Id of the process that owns the window.</param>
/// <param name="Started">When that process started, as the Host counts time. A process id is handed out again too. Zero when it could not be read.</param>
/// <param name="Way">How the window was taken off the screen, which is how it is put back.</param>
public readonly record struct HiddenWindow(long Id, int Process, long Started, HideWay Way);

/// <summary>What a recorded handle names now.</summary>
/// <param name="Process">Id of the process that owns the window now.</param>
/// <param name="Started">When that process started. Zero when it could not be read.</param>
/// <param name="Visible">True when Windows calls the window visible. A cloaked window is.</param>
/// <param name="Cloaked">True while the shell's cloak is on the window: Winoma's, or the one Windows puts on a window of another virtual desktop.</param>
public readonly record struct HiddenReading(int Process, long Started, bool Visible, bool Cloaked);

/// <summary>
/// The windows a desktop has hidden: the scratchpad's while it is closed, and each window a placement hid.
/// The desktop writes this record before it hides a window, because a desktop that is killed cannot show them on its way out.
/// The desktop watcher and the next desktop read it and put back what is <see cref="Stranded"/>. After a desktop that was replaced,
/// the next one takes those windows over still hidden. The lifetime file is what says a desktop is being replaced.
/// The first line is <c>desktop</c> and the process id of the desktop that wrote it.
/// Each line after is a handle, its process id, that process's start time, and <c>cloak</c> or <c>hide</c>.
/// </summary>
public sealed class HiddenWindows
{
    /// <summary>The record's file name in the user folder.</summary>
    public const string FileName = "hidden";

    private const string DesktopWord = "desktop";
    private const string CloakWord = "cloak";
    private const string HideWord = "hide";

    /// <summary>A record of <paramref name="windows"/>, written by <paramref name="desktop"/>.</summary>
    /// <param name="desktop">Process id of the desktop that hid the windows.</param>
    /// <param name="windows">The hidden windows.</param>
    public HiddenWindows(int desktop, IReadOnlyList<HiddenWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        Desktop = desktop;
        Windows = windows;
    }

    /// <summary>Process id of the desktop that wrote the record. Zero when the record does not say.</summary>
    public int Desktop { get; }

    /// <summary>The windows that desktop had hidden.</summary>
    public IReadOnlyList<HiddenWindow> Windows { get; }

    /// <summary>
    /// True when <paramref name="desktop"/> may put this record's windows back. The desktop watcher asks with the desktop it watched,
    /// since a newer desktop may have taken the record over or be hiding windows of its own. A desktop that starts is the only one, and takes any.
    /// </summary>
    /// <param name="desktop">Process id of the desktop that exited. Null takes any record.</param>
    /// <returns>False for a record another desktop wrote.</returns>
    public bool WrittenBy(int? desktop) => desktop is not int pid || Desktop == pid;

    // A rescue asks an Explorer that cannot be reached ten times more, 300 milliseconds apart.
    private const int RescueWaits = 10;
    private static readonly TimeSpan RescueWaitTime = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How long a rescue waits before it asks Explorer to uncloak a window again. It waits only for an Explorer that could not be reached:
    /// a desktop can die with Explorer, which takes a moment to come back, and the cloak outlives both. One count serves every window
    /// of the rescue, so a rescue is held three seconds at most.
    /// </summary>
    /// <param name="refusal">Explorer's answer to the uncloak.</param>
    /// <param name="waited">How many times this rescue has waited already, for any of its windows.</param>
    /// <returns>The wait before the next try. Null when the window is left cloaked, and the record keeps it for the next reader.</returns>
    public static TimeSpan? RescueWait(CloakRefusal refusal, int waited) =>
        refusal == CloakRefusal.Unreachable && waited is >= 0 and < RescueWaits ? RescueWaitTime : null;

    /// <summary>The record as the file holds it.</summary>
    public string Text()
    {
        var text = new StringBuilder();
        text.Append(DesktopWord).Append(' ').Append(Desktop.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var window in Windows)
        {
            text.Append(window.Id.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(window.Process.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(window.Started.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(window.Way == HideWay.Cloak ? CloakWord : HideWord).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Reads a record. A line that is not whole is left out, so a file cut short still gives the windows before the cut.</summary>
    /// <param name="text">The file's text. Null or empty is a record of no windows.</param>
    /// <returns>The record.</returns>
    public static HiddenWindows Read(string? text)
    {
        var desktop = 0;
        var windows = new List<HiddenWindow>();
        foreach (var line in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0] == DesktopWord)
            {
                _ = int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out desktop);
            }
            else if (parts.Length == 4
                && long.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var process)
                && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var started)
                && parts[3] is CloakWord or HideWord)
            {
                windows.Add(new HiddenWindow(id, process, started, parts[3] == CloakWord ? HideWay.Cloak : HideWay.Hide));
            }
        }

        return new HiddenWindows(desktop, windows);
    }

    /// <summary>
    /// The recorded windows to put back: each one that is still open, still owned by the process that owned it when it was hidden,
    /// and still hidden the way the record says. A handle that now names another program's window is left alone,
    /// and so is a window that is off the screen some other way: its program hid it, or Windows has it on another virtual desktop.
    /// </summary>
    /// <param name="read">Reads what a handle names now. Null when it names no window.</param>
    /// <returns>The windows to put back, each with the way to undo, in the order they were recorded.</returns>
    public IReadOnlyList<HiddenWindow> Stranded(Func<long, HiddenReading?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var stranded = new List<HiddenWindow>();
        foreach (var window in Windows)
        {
            if (read(window.Id) is not { } now || now.Process != window.Process || now.Started != window.Started)
            {
                continue;
            }

            if (window.Way == HideWay.Cloak ? now.Cloaked : !now.Visible)
            {
                stranded.Add(window);
            }
        }

        return stranded;
    }
}
