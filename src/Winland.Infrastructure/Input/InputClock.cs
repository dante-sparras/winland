using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Infrastructure;

/// <summary>
/// Recent clicks and keys. A move of the pointer is not input for focus.
/// Once a hook is watching, only those clicks and keys count. A click or key is compared with the event
/// by the times the system stamped on both, so an event handled late is still judged by when it happened.
/// </summary>
internal static class InputClock
{
    internal const uint RecentMilliseconds = 250;

    private static int _watching;

    // The hook's own time stamp of the last click or key, or -1 before the first.
    private static long _lastClickOrKey = -1;

    // The top-level window the last click landed on, or 0 when the last input was a key.
    private static nint _clicked;

    internal static void Watch() => Volatile.Write(ref _watching, 1);

    internal static void NoteKey(uint time, int key, bool injected)
    {
        // Assistive input counts too. An injected modifier alone must not grant an activation serial.
        if (!injected || key is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5))
            NoteClickOrKey(time);
    }

    internal static void NoteClickOrKey(uint time, nint clicked = 0)
    {
        Volatile.Write(ref _clicked, clicked);
        Volatile.Write(ref _lastClickOrKey, time);
    }

    /// <summary>The top-level window the last click landed on, or null when the last input was a key.</summary>
    internal static WindowId? Clicked => Volatile.Read(ref _clicked) is var clicked and not 0 ? new WindowId(clicked) : null;

    internal static bool Recent(uint eventTime)
    {
        if (Volatile.Read(ref _watching) != 0)
        {
            var last = Volatile.Read(ref _lastClickOrKey);
            if (last < 0)
                return false;

            return unchecked(eventTime - (uint)last) <= RecentMilliseconds;
        }

        var info = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>() };
        if (!PInvoke.GetLastInputInfo(ref info))
            return false;

        return unchecked(eventTime - info.dwTime) <= RecentMilliseconds;
    }
}
