using System.Runtime.InteropServices;

namespace Winland.Platform;

/// <summary>
/// Writes the Layout of a window's Workspace and its Group onto that window, and reads them back from there (ADR 0050).
/// The mark is kept nowhere else, so a desktop that was killed loses none. A window of a program that runs as administrator refuses
/// the write, and the elevated helper does not write this one: that window comes back as a plain tile.
/// </summary>
internal static class TilingProperty
{
    // Windows that refused the mark, and were logged for it. Only the log line depends on this set: the write is still tried.
    // The desktop's thread is the only one that comes here.
    private static readonly HashSet<nint> Refused = [];

    /// <summary>Makes <paramref name="hwnd"/> carry <paramref name="mark"/>. A window that already carries it costs one read.</summary>
    /// <param name="hwnd">A top-level window.</param>
    /// <param name="mark">What <see cref="TilingMark.Write"/> gives. <see cref="TilingMark.None"/> takes the mark off.</param>
    public static void Write(nint hwnd, int mark)
    {
        if (hwnd == 0)
        {
            return;
        }

        // A property of zero reads the same as none, so zero is never written: whatever is under Winoma's name goes.
        var carried = Native.GetProp(hwnd, TilingMark.Property);
        if (mark == TilingMark.None ? carried == 0 : carried == mark)
        {
            return;
        }

        var done = mark == TilingMark.None
            ? Native.RemoveProp(hwnd, TilingMark.Property) != 0
            : Native.SetProp(hwnd, TilingMark.Property, mark);
        if (done || !Refused.Add(hwnd))
        {
            return;
        }

        if (Refused.Count > 64)
        {
            Refused.Clear();
            Refused.Add(hwnd);
        }

        Log.Information("Tiling", "could not mark window " + hwnd + " with its layout and group: error " + Marshal.GetLastWin32Error());
    }

    /// <summary>The mark of every open top-level window that carries one. A hidden window is read too: a Group's other members are hidden behind the one shown.</summary>
    /// <returns>Each marked window with its mark.</returns>
    public static Dictionary<long, int> Read()
    {
        var marks = new Dictionary<long, int>();
        Native.EnumWindowsProc visit = (hwnd, _) =>
        {
            var property = Native.GetProp(hwnd, TilingMark.Property);
            if (property > 0 && property <= int.MaxValue && TilingMark.Read(property) is var reading && (reading.Scrolling || reading.Group > 0))
            {
                marks[hwnd] = (int)property;
            }

            return true;
        };
        _ = Native.EnumWindows(visit, 0);
        GC.KeepAlive(visit);
        return marks;
    }
}
