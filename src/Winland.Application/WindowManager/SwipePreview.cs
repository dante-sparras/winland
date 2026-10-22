namespace Winland.Application;

/// <summary>
/// The live part of a workspace swipe: while the fingers move, the Workspace swiped from follows them and the one
/// it goes to slides in beside it, as Winland offsets both. When the fingers lift, the switch or the
/// return animates on from where the windows are.
/// </summary>
public sealed partial class WindowManager
{
    private SwipePreview? _swipePreview;

    // The rectangles a finished swipe left its windows on, which the motions that follow start from.
    private Dictionary<WindowId, Rectangle>? _swipeLanding;

    // Offsets the Workspace swiped from by the swipe, and the neighbour it goes to by one monitor more. The axis is the
    // workspaces animation's: a vertical slide style swipes vertically.
    private Outcome PreviewSwipe(WorkspaceSwipe swipe, WorkspaceId? neighbour)
    {
        var screen = MonitorById(swipe.Monitor);
        if (screen is null && _monitors.Count > 0)
            return View([]);

        var preview = _swipePreview ??= new SwipePreview(swipe.Begin);
        var (vertical, span) = SwipeSpan(swipe.Begin, screen?.Bounds ?? _area);
        var offset = -swipe.Delta / Math.Max(1, _config.Swipe.Distance) * span;
        var motions = new List<Motion>();
        Offset(swipe.Begin, offset, vertical, preview, motions);

        var show = new List<WindowId>();
        var hide = new List<WindowId>();
        if (preview.Neighbour != neighbour)
        {
            hide.AddRange(preview.Revealed);
            foreach (var window in preview.Revealed)
                preview.Shown.Remove(window);
            preview.Revealed.Clear();
            preview.Neighbour = neighbour;
        }

        if (neighbour is WorkspaceId beside && swipe.Delta != 0)
        {
            var besideOffset = offset + (swipe.Delta < 0 ? -span : span);
            foreach (var window in Offset(beside, besideOffset, vertical, preview, motions))
            {
                if (preview.Revealed.Add(window))
                    show.Add(window);
            }
        }

        return View([], show, hide) with { Motions = motions };
    }

    // Each window of the Workspace, but pinned and hidden ones, moved by the offset at once. Returns the windows moved.
    private List<WindowId> Offset(WorkspaceId workspace, double offset, bool vertical, SwipePreview preview, List<Motion> motions)
    {
        var moved = new List<WindowId>();
        if (_workspaces.Find(workspace) is not { } desk)
            return moved;

        var shift = Pixels(offset);
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Minimized || _pins.IsPinned(client.Window) || desk.IsWaiting(client.Window) || !desk.Layout.TryBounds(client.Window, out var slot))
                continue;

            var rest = PlacedBounds(client, desk, slot);
            var at = vertical ? rest with { Y = rest.Y + shift } : rest with { X = rest.X + shift };
            preview.Shown[client.Window] = at;
            motions.Add(new Motion(client.Window, at) { From = at, Warp = true, Rest = rest });
            moved.Add(client.Window);
        }

        return moved;
    }

    // Whether the swipe runs vertically, and how far a whole swipe moves a Workspace: the monitor's width or height
    // and the gap between Workspaces.
    private (bool Vertical, double Span) SwipeSpan(WorkspaceId workspace, Rectangle monitor)
    {
        var style = _config.Animations.StyleOf("workspaces");
        var vertical = style == "slidevert" || style.StartsWith("slidefadevert", StringComparison.Ordinal);
        var between = _workspaces.Find(workspace)?.Options.GapsWorkspaces ?? 0;
        return (vertical, (vertical ? monitor.Height : monitor.Width) + between);
    }

    // The swipe ended without a switch: the Workspace swiped from slides back and the neighbour slides away and hides.
    private Outcome ReturnSwipe(WorkspaceSwipe swipe)
    {
        if (TakePreview() is not { } preview)
            return View([]);

        var (vertical, span) = SwipeSpan(swipe.Begin, MonitorById(swipe.Monitor)?.Bounds ?? _area);
        var back = _config.Animations.Enabled && ReducedMotion?.Invoke() != true ? _config.Animations.TimingOf("workspacesIn") : null;
        var away = _config.Animations.Enabled && ReducedMotion?.Invoke() != true ? _config.Animations.TimingOf("workspacesOut") : null;
        var motions = new List<Motion>();
        var hide = new List<WindowId>();
        var side = Pixels(swipe.Delta < 0 ? -span : span);
        foreach (var (window, at) in preview.Shown)
        {
            if (Find(window) is not { } client || WorkspaceContaining(window) is not { } desk || !desk.Layout.TryBounds(window, out var slot))
                continue;

            var rest = PlacedBounds(client, desk, slot);
            if (!preview.Revealed.Contains(window))
            {
                motions.Add(new Motion(window, rest) { From = at, Warp = true, Move = back });
                continue;
            }

            var gone = vertical ? rest with { Y = rest.Y + side } : rest with { X = rest.X + side };
            motions.Add(new Motion(window, gone) { From = at, Warp = true, Move = away, End = MotionEnd.Hide, Rest = rest });
            hide.Add(window);
        }

        return View([], hide: hide) with { Motions = motions };
    }

    // The swipe switched Workspace: the motions the switch plans start from where the swipe left each window.
    private void LandSwipe() => _swipeLanding = TakePreview()?.Shown;

    private SwipePreview? TakePreview()
    {
        var preview = _swipePreview;
        _swipePreview = null;
        return preview;
    }

    // Starts the switch's motions from the swipe's last rectangles. A window the switch does not animate is placed
    // where it belongs at once.
    private IReadOnlyList<Motion> FromSwipe(IReadOnlyList<Motion> motions)
    {
        var landing = _swipeLanding;
        _swipeLanding = null;
        if (landing is null)
            return motions;

        var started = new Motion[motions.Count];
        for (var i = 0; i < motions.Count; i++)
        {
            var motion = motions[i];
            started[i] = landing.TryGetValue(motion.Window, out var at) && motion.Move is not null
                ? motion with { From = at, Warp = true }
                : motion;
        }

        return started;
    }

    private sealed class SwipePreview(WorkspaceId begin)
    {
        public WorkspaceId Begin { get; } = begin;

        public WorkspaceId? Neighbour { get; set; }

        /// <summary>Where each window the swipe moved is now.</summary>
        public Dictionary<WindowId, Rectangle> Shown { get; } = [];

        /// <summary>The neighbour's windows the swipe showed, which a return hides again.</summary>
        public HashSet<WindowId> Revealed { get; } = [];
    }
}
