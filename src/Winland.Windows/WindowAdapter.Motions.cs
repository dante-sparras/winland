using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

public sealed partial class WindowAdapter
{
    private const uint CloseMessage = 0x0010;

    private static readonly MotionPlayer Motions = new(new Win32MotionTarget());

    // Splits an outcome's motions by the step of Apply that starts them.
    private static Dictionary<WindowId, Motion> MotionsOf(Outcome outcome)
    {
        var motions = new Dictionary<WindowId, Motion>(outcome.Motions.Count);
        foreach (var motion in outcome.Motions)
            motions[motion.Window] = motion;
        return motions;
    }

    private static void ConcealOrLeave(WindowId window, Dictionary<WindowId, Motion> motions)
    {
        lock (Motions.For(window))
        {
            if (motions.Remove(window, out var motion) && motion.End == MotionEnd.Hide && Motions.Begin(motion))
                return;

            Motions.Halt(window);
            Conceal(window);
        }
    }

    private static void PlaceMoving(WindowId window, Rectangle bounds, bool borderless, Dictionary<WindowId, Motion> motions)
    {
        lock (Motions.For(window))
        {
            if (motions.Remove(window, out var motion))
            {
                if (Motions.Begin(motion) && motion.Move is not null)
                    return;

                PlaceFrame(window, bounds, borderless);
                return;
            }

            if (Motions.Settle(window, bounds))
                return;

            PlaceFrame(window, bounds, borderless);
        }
    }

    private static void PlaceFrame(WindowId window, Rectangle bounds, bool borderless)
    {
        if (borderless)
            Move(new HWND((nint)window.Value), bounds);
        else
            Place(window, bounds);
    }

    private static void BeginRest(Dictionary<WindowId, Motion> motions)
    {
        foreach (var motion in motions.Values)
        {
            lock (Motions.For(motion.Window))
                _ = Motions.Begin(motion);
        }

        motions.Clear();
    }

    private static void CloseOrLeave(WindowId window, Dictionary<WindowId, Motion> motions)
    {
        lock (Motions.For(window))
        {
            if (motions.Remove(window, out var motion) && motion.End == MotionEnd.Close && Motions.Begin(motion))
                return;

            _ = PInvoke.PostMessage(new HWND((nint)window.Value), CloseMessage, default, default);
        }
    }

    private static void LandMotions() => Motions.Land();

    private static void DropMotion(WindowId window) => Motions.Drop(window);

    private static unsafe bool ReducedMotion()
    {
        BOOL animate = true;
        return PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &animate, 0)
            && !animate;
    }

    private sealed class Win32MotionTarget : IMotionTarget
    {
        public bool Hung(WindowId window) => PInvoke.IsHungAppWindow(new HWND((nint)window.Value));

        public void Move(WindowId window, Rectangle bounds)
        {
            var hwnd = new HWND((nint)window.Value);
            Interlocked.Increment(ref Applying);
            try
            {
                var insets = InsetsFor(window, hwnd, (int)PInvoke.GetDpiForWindow(hwnd));
                WindowAdapter.Move(hwnd, insets.OuterFor(bounds));
            }
            finally
            {
                Interlocked.Decrement(ref Applying);
            }
        }

        public void Place(WindowId window, Rectangle bounds)
        {
            Interlocked.Increment(ref Applying);
            try
            {
                WindowAdapter.Place(window, bounds);
            }
            finally
            {
                Interlocked.Decrement(ref Applying);
            }
        }

        public void Alpha(WindowId window, byte alpha) => Opacity.Apply(window, alpha);

        public void Border(WindowId window, BorderColor color)
        {
            // The exit cleanup put the border back. A motion step after that would leave its color.
            if (Releasing)
                return;

            SetAttribute(new HWND((nint)window.Value), BorderColorAttribute, ColorRef(color));
        }

        public void Hide(WindowId window) => Conceal(window);

        public void Close(WindowId window) =>
            _ = PInvoke.PostMessage(new HWND((nint)window.Value), CloseMessage, default, default);

        public bool Shown(WindowId window)
        {
            var hwnd = new HWND((nint)window.Value);
            return PInvoke.IsWindow(hwnd) && PInvoke.IsWindowVisible(hwnd);
        }

        public void Pace()
        {
            // DwmFlush returns after the next composition pass. Without composition it fails at once.
            if (PInvoke.DwmFlush().Failed)
                Thread.Sleep(15);
        }
    }
}
