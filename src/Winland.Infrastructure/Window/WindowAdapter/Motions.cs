using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Infrastructure;

public sealed partial class WindowAdapter
{
    // Splits an outcome's motions by the step of Apply that starts them.
    private static Dictionary<WindowId, Motion> MotionsOf(Outcome outcome)
    {
        var motions = new Dictionary<WindowId, Motion>(outcome.Motions.Count);
        foreach (var motion in outcome.Motions)
            motions[motion.Window] = motion;
        return motions;
    }

    /// <summary>Steps the motions once, as their thread does, and returns whether any is still running. Only an adapter made unthreaded is stepped this way.</summary>
    internal bool Tick() => _motions.Tick();

    private void ConcealOrLeave(WindowId window, Dictionary<WindowId, Motion> motions)
    {
        lock (_motions.For(window))
        {
            if (motions.Remove(window, out var motion) && motion.End == MotionEnd.Hide && _motions.Begin(motion))
                return;

            _motions.Halt(window);
            Conceal(window);
        }
    }

    private void PlaceMoving(WindowId window, Rectangle bounds, bool borderless, Dictionary<WindowId, Motion> motions)
    {
        lock (_motions.For(window))
        {
            if (motions.Remove(window, out var motion))
            {
                // A window that fades in is put where its motion starts first, and waited for. Moved without waiting,
                // a window that just opened takes the move only when its program gets to it, and fades in where it
                // opened meanwhile. One still moving carries on from where it is, so it is not put back to the start.
                if (motion.FadeFrom == 0 && (motion.Warp || !_motions.Running(window)))
                    PlaceFrame(window, motion.From ?? bounds, borderless);

                // A fade or a border change with no move of its own leaves a move still running toward this rectangle
                // to the player, as a placement with no motion does.
                if (_motions.Begin(motion) && (motion.Move is not null || _motions.Settle(window, bounds)))
                    return;

                PlaceFrame(window, bounds, borderless);
                EndStandIn(window, null);
                return;
            }

            if (_motions.Settle(window, bounds))
                return;

            PlaceFrame(window, bounds, borderless);
            EndStandIn(window, null);
        }
    }

    // A step of a drag still held. The drag's end places the window exactly.
    private void PlaceLive(WindowId window, Rectangle bounds, bool borderless, Dictionary<WindowId, Motion> motions)
    {
        lock (_motions.For(window))
        {
            motions.Remove(window);
            if (_motions.Settle(window, bounds))
                return;

            EndStandIn(window, null);
            QuickPlace(window, bounds, borderless);
        }
    }

    private void PlaceFrame(WindowId window, Rectangle bounds, bool borderless)
    {
        if (!borderless)
        {
            Place(window, bounds);
            return;
        }

        // A borderless window already on its rectangle is left alone, as a framed one is.
        var trails = TryOuter(window, out var before) && _quick.Trails(window, before);
        if (!trails && before == bounds)
            return;

        if (Move(window, bounds) && trails)
            SendQuick(window, bounds, keepSize: false);
    }

    private void BeginRest(Dictionary<WindowId, Motion> motions)
    {
        foreach (var motion in motions.Values)
        {
            lock (_motions.For(motion.Window))
                _ = _motions.Begin(motion);
        }

        motions.Clear();
    }

    private void CloseOrLeave(WindowId window, Dictionary<WindowId, Motion> motions)
    {
        lock (_motions.For(window))
        {
            if (motions.Remove(window, out var motion) && motion.End == MotionEnd.Close && _motions.Begin(motion))
                return;

            if (TryVisible(window, out var rest))
                _motions.Close(window, rest, _opacity.Wanted(window));
            else
                _desktop.Close(window);
        }
    }

    private void LandMotions() => _motions.Land();

    private void DropMotion(WindowId window) => _motions.Drop(window);

    // Every step asks, so the setting is read once and again after Windows broadcasts a change to the settings.
    private static readonly Lock SettingsGate = new();
    private static bool? _reducedMotion;

    private static bool ReducedMotion()
    {
        lock (SettingsGate)
            return _reducedMotion ??= ReadReducedMotion();
    }

    internal static void ForgetSettings()
    {
        lock (SettingsGate)
            _reducedMotion = null;
    }

    // Stryker disable once all : asks Windows for the animation setting.
    private static unsafe bool ReadReducedMotion()
    {
        BOOL animate = true;
        return PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETCLIENTAREAANIMATION, 0, &animate, 0)
            && !animate;
    }

    private sealed class MotionTarget(WindowAdapter adapter) : IMotionTarget
    {
        public bool Hung(WindowId window) => adapter._desktop.Hung(window);

        // An in-between frame does not wait for the window, so one that repaints slowly holds up no other.
        public void Move(WindowId window, Rectangle bounds, Rectangle content)
        {
            if (!adapter.StandFor(window, bounds, content))
                adapter.QuickPlace(window, bounds, adapter._borderless.Owns(window));
        }

        public void PlaceAsync(WindowId window, Rectangle bounds)
        {
            adapter.PlaceAsync(window, bounds, adapter._borderless.Owns(window));
            adapter.EndStandIn(window, null);
        }

        public void Place(WindowId window, Rectangle bounds)
        {
            Interlocked.Increment(ref adapter._applying);
            try
            {
                // A fullscreen window has no frame, so it is placed by its outer rectangle, as every placement of it is.
                adapter.PlaceFrame(window, bounds, adapter._borderless.Owns(window));
                adapter.EndStandIn(window, null);
            }
            finally
            {
                Interlocked.Decrement(ref adapter._applying);
            }
        }

        public void Alpha(WindowId window, byte alpha)
        {
            adapter._opacity.Apply(window, alpha);
            adapter.StandInAlpha(window, alpha);

            // The decorations fade with the window while its motion runs, and are opaque again once it has ended.
            MotionAlpha.Set(window, adapter._motions.Running(window) ? alpha : null);
        }

        public void Border(WindowId window, BorderColor color)
        {
            // The exit cleanup put the border back. A motion step after that would leave its color.
            if (adapter._releasing)
                return;

            BorderHost.Tint(window, color);
        }

        public void Hide(WindowId window)
        {
            adapter.Conceal(window);
            adapter.EndStandIn(window, null);
        }

        public void Release(WindowId window)
        {
            adapter.EndStandIn(window, null);
            MotionAlpha.Set(window, null);
        }

        public void Close(WindowId window)
        {
            // The window stays veiled while it closes, so it does not show for a frame on its Layout rectangle. A
            // window that stays open is placed again, which ends its stand-in, and one that goes is dropped.
            adapter.HideStandIn(window);
            adapter._desktop.Close(window);
        }

        public bool Shown(WindowId window) => adapter._desktop.Exists(window) && adapter._desktop.Shown(window);

        public void Pace() => adapter._desktop.Pace();
    }
}
