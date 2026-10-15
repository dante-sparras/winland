using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Winland.Windows;

/// <summary>
/// Recent clicks and keys. A move of the pointer is not input for focus.
/// Once a hook is watching, only those clicks and keys count.
/// </summary>
internal static class InputClock
{
    internal const uint RecentMilliseconds = 250;

    private static int _watching;
    private static long _lastClickOrKey;

    internal static void Watch() => Volatile.Write(ref _watching, 1);

    internal static void NoteClickOrKey() => Volatile.Write(ref _lastClickOrKey, Environment.TickCount64);

    internal static bool Recent(uint eventTime)
    {
        if (Volatile.Read(ref _watching) != 0)
        {
            var last = Volatile.Read(ref _lastClickOrKey);
            if (last == 0)
                return false;

            var age = Environment.TickCount64 - last;
            return age >= 0 && age <= RecentMilliseconds;
        }

        var info = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>() };
        if (!PInvoke.GetLastInputInfo(ref info))
            return false;

        return unchecked(eventTime - info.dwTime) <= RecentMilliseconds;
    }
}
