
namespace Winland.Platform.Win32;

/// <summary>Lists top-level windows Winoma is allowed to show or tile.</summary>
internal static class WindowCensus
{
    private const uint Owner = 4;

    private static readonly uint Self = (uint)Environment.ProcessId;

    // The list the walk on this thread is filling. A census runs on whichever thread asks, and a walk calls back on that thread.
    [ThreadStatic]
    private static List<WindowReading>? _walk;

    // One delegate for the life of the process, so a census does not build a new native thunk.
    private static readonly Native.EnumWindowsProc Visit = static (hwnd, _) =>
    {
        if (Reading(hwnd, Self) is { } reading)
        {
            _walk!.Add(reading);
        }

        return true;
    };

    /// <summary>
    /// One <c>EnumWindows</c> pass. Only windows that may count are read in full: a hidden window, one under a cloak,
    /// and one of Winoma's own are skipped after a visibility and cloak check. A cloaked window is one its app closed, one Winoma hid,
    /// or one Windows keeps on another virtual desktop, and none of them is on the glass.
    /// </summary>
    internal static IReadOnlyList<WindowReading> Readings()
    {
        var readings = new List<WindowReading>();
        _walk = readings;
        try
        {
            Native.EnumWindows(Visit, 0);
        }
        finally
        {
            _walk = null;
        }

        return readings;
    }

    private static WindowReading? Reading(nint hwnd, uint self)
    {
        var visible = Native.IsWindowVisible(hwnd);
        if (Native.DwmGetWindowAttribute(hwnd, Native.DwmwaCloaked, out int cloak, sizeof(int)) != 0)
        {
            cloak = 0;
        }

        if (!WindowAdmission.MayCount(visible, cloak))
        {
            return null;
        }

        _ = Native.GetWindowThreadProcessId(hwnd, out var process);
        if (process == self || ShellProperty.IsTagged(hwnd))
        {
            return null;
        }

        // Without the helper Winoma cannot move an administrator window, so a tile kept for one would stay an empty hole (ADR 0069).
        // Explorer cloaks it for any process all the same, so it is not left out: it stays Free, and is hidden with its Workspace.
        var untiled = !ElevatedBroker.Allowed && Elevated(hwnd, process);

        return new WindowReading(
            hwnd,
            Native.GetWindowLongPtr(hwnd, Native.GwlStyle),
            Native.GetWindowLongPtr(hwnd, Native.GwlExStyle),
            visible,
            cloak,
            Native.GetWindow(hwnd, Owner) != 0,
            ClassName(hwnd),
            Title(hwnd),
            OwnProcess: false,
            Untiled: untiled);
    }

    private static string ClassName(nint hwnd)
    {
        // Read on the stack. A class name is at most 256 characters.
        Span<char> buffer = stackalloc char[256];
        int length;
        unsafe
        {
            fixed (char* chars = buffer)
            {
                length = Native.GetClassName(hwnd, chars, buffer.Length);
            }
        }

        return length > 0 ? new string(buffer[..length]) : "";
    }

    private static string Title(nint hwnd)
    {
        Span<char> buffer = stackalloc char[512];
        int length;
        unsafe
        {
            fixed (char* chars = buffer)
            {
                length = Native.GetWindowText(hwnd, chars, buffer.Length);
            }
        }

        return length > 0 ? new string(buffer[..length]) : "";
    }

    // Whether a process is elevated does not change while it lives. A process id is handed out again, so an answer is kept for a short while only.
    private const long ElevatedKeptMs = 30_000;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, (bool Elevated, long At)> ElevatedSeen = new();

    /// <summary>True when the window's process runs as administrator. Asked for every window of every census while the helper is not allowed.</summary>
    private static bool Elevated(nint hwnd, uint process)
    {
        var now = Environment.TickCount64;
        if (ElevatedSeen.TryGetValue(process, out var seen) && now - seen.At < ElevatedKeptMs)
        {
            return seen.Elevated;
        }

        var elevated = WindowGateway.IsElevated(hwnd);
        ElevatedSeen[process] = (elevated, now);
        return elevated;
    }
}
