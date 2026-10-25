using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

public sealed partial class WindowAdapter
{
    private const uint CloseMessage = 0x0010;

    private static readonly MotionPlayer Motions = new(new Win32MotionTarget(), PresentClock.Now);

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
                // A window that fades in is put where its motion starts first, and waited for. Moved without waiting,
                // a window that just opened takes the move only when its program gets to it, and fades in where it
                // opened meanwhile.
                if (motion.FadeFrom == 0)
                    PlaceFrame(window, motion.From ?? bounds, borderless);

                if (Motions.Begin(motion) && motion.Move is not null)
                    return;

                PlaceFrame(window, bounds, borderless);
                EndStandIn(window, null);
                return;
            }

            if (Motions.Settle(window, bounds))
                return;

            PlaceFrame(window, bounds, borderless);
            EndStandIn(window, null);
        }
    }

    // A step of a drag still held. The drag's end places the window exactly.
    private static void PlaceLive(WindowId window, Rectangle bounds, bool borderless, Dictionary<WindowId, Motion> motions)
    {
        lock (Motions.For(window))
        {
            motions.Remove(window);
            if (Motions.Settle(window, bounds))
                return;

            EndStandIn(window, null);
            QuickPlace(window, bounds, borderless);
        }
    }

    private static void PlaceFrame(WindowId window, Rectangle bounds, bool borderless)
    {
        if (!borderless)
        {
            Place(window, bounds);
            return;
        }

        var hwnd = new HWND((nint)window.Value);
        var trails = TryOuter(hwnd, out var before) && Quick.Trails(window, before);
        if (Move(hwnd, bounds) && trails)
            SendQuick(hwnd, bounds, keepSize: false);
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

            if (TryVisible(new HWND((nint)window.Value), out var rest))
                Motions.Close(window, rest, Opacity.Wanted(window));
            else
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

        // An in-between frame does not wait for the window, so one that repaints slowly holds up no other.
        public void Move(WindowId window, Rectangle bounds, Rectangle content)
        {
            if (!StandFor(window, bounds, content))
                QuickPlace(window, bounds, Borderless.Owns(window));
        }

        public void PlaceAsync(WindowId window, Rectangle bounds)
        {
            WindowAdapter.PlaceAsync(window, bounds, Borderless.Owns(window));
            EndStandIn(window, null);
        }

        public void Place(WindowId window, Rectangle bounds)
        {
            Interlocked.Increment(ref Applying);
            try
            {
                // A fullscreen window has no frame, so it is placed by its outer rectangle, as every placement of it is.
                PlaceFrame(window, bounds, Borderless.Owns(window));
                EndStandIn(window, null);
            }
            finally
            {
                Interlocked.Decrement(ref Applying);
            }
        }

        public void Alpha(WindowId window, byte alpha)
        {
            Opacity.Apply(window, alpha);
            StandInAlpha(window, alpha);

            // The decorations fade with the window while its motion runs, and are opaque again once it has ended.
            MotionAlpha.Set(window, Motions.Running(window) ? alpha : null);
        }

        public void Border(WindowId window, BorderColor color)
        {
            // The exit cleanup put the border back. A motion step after that would leave its color.
            if (Releasing)
                return;

            BorderHost.Tint(window, color);
        }

        public void Hide(WindowId window)
        {
            Conceal(window);
            EndStandIn(window, null);
        }

        public void Release(WindowId window)
        {
            EndStandIn(window, null);
            MotionAlpha.Set(window, null);
        }

        public void Close(WindowId window)
        {
            // The window stays veiled while it closes, so it does not show for a frame on its Layout rectangle. A
            // window that stays open is placed again, which ends its stand-in, and one that goes is dropped.
            HideStandIn(window);
            _ = PInvoke.PostMessage(new HWND((nint)window.Value), CloseMessage, default, default);
        }

        public bool Shown(WindowId window)
        {
            var hwnd = new HWND((nint)window.Value);
            return PInvoke.IsWindow(hwnd) && PInvoke.IsWindowVisible(hwnd);
        }

        public void Pace()
        {
            if (PresentClock.WaitForFrame())
                return;

            // DwmFlush returns after the next composition pass. Without composition it fails at once.
            if (PInvoke.DwmFlush().Failed)
                Thread.Sleep(15);
        }
    }
}
