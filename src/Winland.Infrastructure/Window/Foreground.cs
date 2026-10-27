using System.Globalization;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

/// <summary>
/// Tells a focus change that followed our own request apart from a click or key and from an application taking the foreground.
/// Putting the foreground back can fail while another process holds it; the failure is reported and the window is flashed.
/// </summary>
internal static class Foreground
{
    private const byte MenuKey = 0x12;

    private static readonly Lock Gate = new();
    private static WindowId? _expected;
    private static long _expectedUntil;

    internal static FocusCause Classify(WindowId window, uint eventTime)
    {
        lock (Gate)
        {
            if (_expected == window && Environment.TickCount64 <= _expectedUntil)
            {
                _expected = null;
                return FocusCause.Requested;
            }
        }

        return InputClock.Recent(eventTime) ? FocusCause.User : FocusCause.Activated;
    }

    internal static bool MoveTo(WindowId window)
    {
        var hwnd = new HWND((nint)window.Value);
        if (hwnd.IsNull)
            return false;

        if (PInvoke.GetForegroundWindow() == hwnd)
            return true;

        // Giving the foreground waits on the window's thread, and on the old foreground window's, so a program that
        // stopped answering would hold every bind behind it. A window another virtual desktop cloaks would take the
        // user to that desktop.
        if (PInvoke.IsHungAppWindow(hwnd) || WindowAdapter.IsCloaked(hwnd))
            return false;

        Expect(window);
        if (TrySet(hwnd))
            return true;

        if (TryAttached(hwnd))
            return true;

        TapMenuKey();
        if (TrySet(hwnd))
            return true;

        Flash(hwnd);
        Console.Error.WriteLine(
            "Could not restore focus to window 0x"
            + window.Value.ToString("x", CultureInfo.InvariantCulture)
            + ".");
        return false;
    }

    /// <summary>
    /// Gives the foreground to the desktop, when Winland hid the window that had it and focuses none, so Windows
    /// does not pick a window of its own to activate.
    /// </summary>
    internal static void ToDesktop()
    {
        var shell = PInvoke.GetShellWindow();
        if (!shell.IsNull && PInvoke.GetForegroundWindow() != shell)
            _ = MoveTo(new WindowId((nint)shell));
    }

    private static void Expect(WindowId window)
    {
        lock (Gate)
        {
            _expected = window;
            _expectedUntil = Environment.TickCount64 + 1_000;
        }
    }

    // SetForegroundWindow brings the window to the top when it succeeds.
    private static bool TrySet(HWND hwnd)
    {
        _ = PInvoke.SetForegroundWindow(hwnd);
        return PInvoke.GetForegroundWindow() == hwnd;
    }

    private static bool TryAttached(HWND hwnd)
    {
        var foreground = PInvoke.GetForegroundWindow();
        var foregroundThread = PInvoke.GetWindowThreadProcessId(foreground, out _);
        var currentThread = PInvoke.GetCurrentThreadId();
        if (foregroundThread == 0 || foregroundThread == currentThread)
            return TrySet(hwnd);

        // Deactivating a hung foreground window would wait on it.
        if (PInvoke.IsHungAppWindow(foreground))
            return false;

        if (!PInvoke.AttachThreadInput(foregroundThread, currentThread, true))
            return false;

        try
        {
            return TrySet(hwnd);
        }
        finally
        {
            _ = PInvoke.AttachThreadInput(foregroundThread, currentThread, false);
        }
    }

    private static void TapMenuKey() => KeyboardHook.Send(MenuTap(altHeld: PInvoke.GetAsyncKeyState(MenuKey) < 0));

    // Tagged, so Winland's own hook does not take it for the user's Alt. Not extended: an extended
    // Alt is Right Alt, which is AltGr, with a fake Left Ctrl, on many layouts. The mask key between its edges
    // keeps the Alt from opening the focused window's menu bar or Office's key tips. An Alt the user already
    // holds unlocks the foreground itself, and is left down.
    internal static INPUT[] MenuTap(bool altHeld) =>
        altHeld
            ? [KeyboardHook.Key(KeyboardHook.MaskKey, up: false), KeyboardHook.Key(KeyboardHook.MaskKey, up: true)]
            :
            [
                KeyboardHook.Key(MenuKey, up: false),
                KeyboardHook.Key(KeyboardHook.MaskKey, up: false),
                KeyboardHook.Key(KeyboardHook.MaskKey, up: true),
                KeyboardHook.Key(MenuKey, up: true),
            ];

    private static unsafe void Flash(HWND hwnd)
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)sizeof(FLASHWINFO),
            hwnd = hwnd,
            dwFlags = FLASHWINFO_FLAGS.FLASHW_ALL,
            uCount = 3,
            dwTimeout = 0,
        };
        _ = PInvoke.FlashWindowEx(&info);
    }
}
