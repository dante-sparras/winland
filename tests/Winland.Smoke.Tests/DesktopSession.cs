using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace Winland.Smoke.Tests;

internal static partial class DesktopSession
{
    // A service or CI host has no desktop: session 0, a window station that is not
    // visible, or a missing session name with nothing in the foreground. A desktop
    // session can leave the session name unset while its window station is visible.
    public static string? SkipReason()
    {
        if (Process.GetCurrentProcess().SessionId == 0)
            return "No interactive desktop is available (session 0).";

        if (!WindowStationVisible())
            return "No interactive desktop is available (no foreground window station).";

        var sessionName = Environment.GetEnvironmentVariable("SESSIONNAME");
        if (string.IsNullOrWhiteSpace(sessionName) && PInvoke.GetForegroundWindow().IsNull)
            return "No interactive desktop is available (SESSIONNAME is missing and there is no foreground window).";

        return null;
    }

    private static bool WindowStationVisible()
    {
        var station = (IntPtr)PInvoke.GetProcessWindowStation();
        if (station == IntPtr.Zero)
            return false;

        var flags = default(UserObjectFlags);
        if (!GetUserObjectInformation(station, 1, ref flags, Marshal.SizeOf<UserObjectFlags>(), out _))
            return false;

        return (flags.Flags & 0x1) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UserObjectFlags
    {
        public int Inherit;
        public int Reserved;
        public int Flags;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetUserObjectInformation(IntPtr station, int index, ref UserObjectFlags flags, int length, out int needed);
}
