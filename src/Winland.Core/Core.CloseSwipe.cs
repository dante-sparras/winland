namespace Winland;

/// <summary>
/// The close gesture, as the pinned release plays it: while the fingers move, the focused window fades and shrinks
/// toward its <c>windowsOut</c> end. When they lift, a swipe of 20 percent of 200 pixels closes it from there, and a
/// shorter one brings it back. A window that has not closed after <c>gestures:close_max_timeout</c> is put back.
/// </summary>
public sealed partial class Core
{
    private const double CloseSwipeDistance = 200;
    private const double CloseSwipeRatio = 0.2;

    private CloseSwipe? _closeSwipe;

    private void BeginCloseSwipe()
    {
        _closeSwipe = null;
        if (ActiveWindow() is not WindowId window || Find(window) is not { } client || WorkspaceContaining(window) is not { } desk
            || !desk.Layout.TryBounds(window, out var slot))
            return;

        var rest = PlacedBounds(client, desk, slot);
        var gone = MotionStyles.Window(_animations.StyleOf("windowsOut"), rest, MonitorRectangle(client.Workspace));
        _closeSwipe = new CloseSwipe(window, rest, gone);
    }

    private Outcome StepCloseSwipe(double distance)
    {
        if (_closeSwipe is not { } swipe || Find(swipe.Window) is null)
            return View([]);

        swipe.Distance += distance;
        var (at, fade) = swipe.Shown();
        return View([]) with { Motions = [new Motion(swipe.Window, at) { From = at, Warp = true, FadeFrom = fade, FadeTo = fade, Rest = swipe.Rest }] };
    }

    private Outcome EndCloseSwipe()
    {
        var swipe = _closeSwipe;
        _closeSwipe = null;
        if (swipe is null || Find(swipe.Window) is null)
            return View([]);

        var animate = _animations.Enabled && ReducedMotion?.Invoke() != true;
        var (at, fade) = swipe.Shown();
        if (swipe.Distance < CloseSwipeDistance * CloseSwipeRatio)
        {
            var back = new Motion(swipe.Window, swipe.Rest)
            {
                From = at,
                Warp = true,
                Move = animate ? _animations.TimingOf("windowsMove") : null,
                FadeFrom = fade,
                Fade = animate ? _animations.TimingOf("fade") : null,
            };
            return View([]) with { Motions = [back] };
        }

        // The window closes from where the swipe left it, and comes back if it is still open after the timeout.
        var closed = ExecuteTriggered("killactive", "", trigger: null);
        var leave = new Motion(swipe.Window, swipe.Gone)
        {
            From = at,
            Warp = true,
            Move = animate ? _animations.TimingOf("windowsOut") : null,
            FadeFrom = fade,
            FadeTo = 0,
            Fade = animate ? _animations.TimingOf("fadeOut") : null,
            End = MotionEnd.Close,
            Rest = swipe.Rest,
            CloseGraceMilliseconds = _swipe.CloseMaxTimeout,
        };
        return closed with { Motions = [leave, .. closed.Motions.Where(motion => motion.Window != swipe.Window)] };
    }

    private sealed class CloseSwipe(WindowId window, Rectangle rest, Rectangle gone)
    {
        public WindowId Window { get; } = window;

        public Rectangle Rest { get; } = rest;

        /// <summary>Where the window ends when it closes, as its <c>windowsOut</c> animation leaves it.</summary>
        public Rectangle Gone { get; } = gone;

        public double Distance { get; set; }

        /// <summary>The rectangle and fade the swipe shows now, partway from the rest to the end.</summary>
        public (Rectangle At, double Fade) Shown()
        {
            var t = Math.Clamp(Distance / CloseSwipeDistance, 0, 1);
            int Lerp(int from, int to) => (int)Math.Round(from + ((to - from) * t), MidpointRounding.AwayFromZero);
            var at = new Rectangle(Lerp(Rest.X, Gone.X), Lerp(Rest.Y, Gone.Y), Lerp(Rest.Width, Gone.Width), Lerp(Rest.Height, Gone.Height));
            return (at, 1 - t);
        }
    }
}
