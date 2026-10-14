using System.Runtime.InteropServices;

namespace Winland.Platform;

/// <summary>
/// Writes the Workspace a window is on onto that window, and reads it back from there (ADR 0078).
/// The mark is kept nowhere else, so a desktop that was killed loses none, and a handle Windows hands to a later window names no old mark.
/// An administrator window refuses the write from a desktop that is not one, and the elevated helper writes it then (ADR 0065).
/// </summary>
internal static class WorkspaceProperty
{
    // Windows that refused the mark with no helper to write it, and were logged for it. Every arrange asks again for every tile.
    // Only the log line depends on this set: the write is still tried. The desktop's thread is the only one that comes here.
    private static readonly HashSet<nint> Refused = [];

    // The mark last handed to the helper for each window. The helper writes a moment later, and until the window carries the mark
    // every arrange comes here again: the helper is asked once for each mark while it runs.
    private static readonly Dictionary<nint, int> Handed = [];

    /// <summary>Makes <paramref name="hwnd"/> carry <paramref name="mark"/>. A window that already carries it costs one read.</summary>
    /// <param name="hwnd">A top-level window.</param>
    /// <param name="mark">A Workspace number or <see cref="WorkspaceMark.Scratchpad"/>. <see cref="WorkspaceMark.None"/> takes the mark off.</param>
    public static void Write(nint hwnd, int mark)
    {
        if (hwnd == 0)
        {
            return;
        }

        // A property of zero reads the same as none, so zero is never written: whatever is under Winoma's name goes.
        var carried = Native.GetProp(hwnd, WorkspaceMark.Property);
        if (mark == WorkspaceMark.None ? carried == 0 : carried == mark)
        {
            Handed.Remove(hwnd);
            return;
        }

        if (Apply(hwnd, mark, out var error))
        {
            return;
        }

        // Access denied: the window belongs to a program that runs as administrator. The helper may write there.
        if (WindowExecutor.Mark(hwnd, mark, succeeded: false, error) is ElevatedAct act)
        {
            if (ElevatedBroker.Running && Handed.TryGetValue(hwnd, out var handed) && handed == mark)
            {
                return;
            }

            if (ElevatedBroker.Mark((nint)act.Id, act.Value))
            {
                if (Handed.Count > 64)
                {
                    Handed.Clear();
                }

                Handed[hwnd] = mark;
                return;
            }
        }

        if (!Refused.Add(hwnd))
        {
            return;
        }

        if (Refused.Count > 64)
        {
            Refused.Clear();
            Refused.Add(hwnd);
        }

        Log.Information("Workspace", "could not mark window " + hwnd + " with workspace " + mark);
    }

    /// <summary>
    /// Writes <paramref name="mark"/> on <paramref name="hwnd"/>, or takes the mark off, with no read first.
    /// The desktop comes here after its read, and the elevated helper for each mark the desktop was refused.
    /// </summary>
    /// <param name="hwnd">A top-level window.</param>
    /// <param name="mark">A Workspace number or <see cref="WorkspaceMark.Scratchpad"/>. <see cref="WorkspaceMark.None"/> takes the mark off.</param>
    /// <param name="error">The Win32 error when this returns false.</param>
    /// <returns>False when Windows refused.</returns>
    internal static bool Apply(nint hwnd, int mark, out int error)
    {
        var done = mark == WorkspaceMark.None
            ? Native.RemoveProp(hwnd, WorkspaceMark.Property) != 0
            : Native.SetProp(hwnd, WorkspaceMark.Property, mark);
        error = done ? 0 : Marshal.GetLastWin32Error();
        return done;
    }

    /// <summary>
    /// The mark of every open top-level window that carries one. A hidden window is read too: the scratchpad's windows are hidden
    /// while it is closed, and they carry the mark a desktop that starts looks for.
    /// </summary>
    /// <returns>Each marked window with its mark.</returns>
    public static Dictionary<long, int> Read()
    {
        var marks = new Dictionary<long, int>();
        Native.EnumWindowsProc visit = (hwnd, _) =>
        {
            var mark = WorkspaceMark.Read(Native.GetProp(hwnd, WorkspaceMark.Property));
            if (mark != WorkspaceMark.None)
            {
                marks[hwnd] = mark;
            }

            return true;
        };
        _ = Native.EnumWindows(visit, 0);
        GC.KeepAlive(visit);
        return marks;
    }
}
