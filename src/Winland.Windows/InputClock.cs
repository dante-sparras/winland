using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Windows;

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

    internal static void Watch() => Volatile.Write(ref _watching, 1);

    internal static void NoteClickOrKey(uint time) => Volatile.Write(ref _lastClickOrKey, time);

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
