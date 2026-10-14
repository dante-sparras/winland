
namespace Winland.Platform;

/// <summary>
/// Writes the caption buttons and frames Winoma takes off a window onto that window, and puts them back from there (ADR 0074).
/// Nothing is kept in this process, so a desktop that was killed loses no record, and a desktop that stops, the desktop watcher,
/// and <c>winoma stop</c> all restore the same way.
/// </summary>
internal static class CaptionReturn
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;

    /// <summary>Records a style change Winoma is about to make to <paramref name="hwnd"/>.</summary>
    public static void Note(nint hwnd, long before, long after)
    {
        var known = Carried(hwnd);
        var bits = CaptionLedger.Marked(known, before, after);

        // A property of zero reads the same as none, and a change that took nothing off has nothing to write.
        if (bits == known)
        {
            return;
        }

        if (!Native.SetProp(hwnd, CaptionLedger.Property, (nint)bits))
        {
            Log.Debug("Tiling", "could not mark window " + hwnd + " with its caption bits");
        }
    }

    /// <summary>Gives every open window the bits Winoma took off it. Any process may call this: the record is on the windows.</summary>
    public static void Restore()
    {
        var restored = 0;
        Native.EnumWindows(
            (hwnd, _) =>
            {
                if (Give(hwnd))
                {
                    restored++;
                }

                return true;
            },
            0);
        Log.Information("Tiling", "gave " + restored + " windows their caption buttons back");
    }

    /// <summary>Gives <paramref name="hwnd"/> the bits Winoma took off it, when it carries any.</summary>
    /// <param name="hwnd">A window.</param>
    /// <returns>True when its style changed.</returns>
    public static bool Give(nint hwnd)
    {
        var bits = Carried(hwnd);

        // The style change waits on the window's own thread. One that has stopped answering keeps its mark for the next try.
        if (bits == 0 || Native.IsHungAppWindow(hwnd))
        {
            return false;
        }

        var style = (long)Native.GetWindowLongPtr(hwnd, Native.GwlStyle);
        if (!CaptionLedger.Returns(style, bits, out var next))
        {
            // A window that went fullscreen in its own app keeps the mark: it may be a tile again.
            return false;
        }

        Native.SetWindowLongPtr(hwnd, Native.GwlStyle, (nint)next);
        Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, Native.SwpNoZOrder | Native.SwpNoActivate | Native.SwpFrameChanged | SwpNoSize | SwpNoMove);
        Native.RemoveProp(hwnd, CaptionLedger.Property);
        return true;
    }

    // A style is 32 bits. The property is the size of a pointer, and only the low half is Winoma's.
    private static long Carried(nint hwnd) => (long)(uint)(ulong)Native.GetProp(hwnd, CaptionLedger.Property);
}
