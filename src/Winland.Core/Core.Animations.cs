namespace Winland;

public sealed partial class Core
{
    private AnimationOptions _animations = AnimationOptions.Documented;
    private readonly Dictionary<WindowId, Decoration> _painted = [];

    /// <summary>Reads the system's reduced motion setting. When it returns true, nothing animates.</summary>
    public Func<bool>? ReducedMotion { get; set; }

    private void ReadAnimations(ConfigDocument document) =>
        _animations = AnimationOptions.From(document);

    // Plans the motions for an outcome by comparing it with the state before it. The placements,
    // shows and hides stay as they are; a motion only says how the adapter gets the window there.
    private Outcome Animate(Outcome outcome, State before)
    {
        var released = _released;
        _released = null;
        var after = outcome.State;
        var windows = new Dictionary<ClientId, Client>(after.Clients.Count);
        foreach (var client in after.Clients)
            windows[client.Id] = client;

        var previous = new Dictionary<WindowId, Decoration>(_painted);
        _painted.Clear();
        foreach (var decoration in outcome.Decorations)
        {
            if (windows.TryGetValue(decoration.Client, out var client))
                _painted[client.Window] = decoration;
        }

        if (!_animations.Enabled || ReducedMotion?.Invoke() == true)
        {
            _swipeLanding = null;
            return outcome;
        }

        // A live swipe's motions are already planned, and win over anything planned here for their windows.
        var own = outcome.Motions;
        var plan = new MotionPlan(this, before, after, outcome, released);
        plan.Workspaces(outcome.Hide, entering: false);
        plan.Workspaces(outcome.Show, entering: true);
        foreach (var closing in outcome.Closes)
            plan.Close(closing);

        // A window the pointer drags follows it at once, and the windows its Layout moves to make room for it, when it
        // is lifted out or dropped, animate as in the pinned release. A mouse resize moves every window at once.
        if (_gesture is not { Resize: true } && _border is null && _nativeSize is null && !InstantResize(outcome, before))
        {
            var held = _gesture?.Window;
            foreach (var placement in outcome.Placements)
            {
                if (windows.TryGetValue(placement.Client, out var client) && client.Window != held)
                    plan.Place(client, placement.Bounds);
            }
        }

        plan.Switches(previous, _painted);
        if (plan.Count == 0)
        {
            _swipeLanding = null;
            return outcome;
        }

        // A window with no_anim moves, fades and changes border color at once, as the pinned release warps it.
        var planned = FromSwipe(plan.Finish(_painted)).Where(motion => OwnProp(motion.Window, "no_anim") is not true).ToList();
        if (own.Count == 0)
            return outcome with { Motions = planned };

        var mine = new HashSet<WindowId>(own.Select(motion => motion.Window));
        return outcome with { Motions = [.. own, .. planned.Where(motion => !mine.Contains(motion.Window))] };
    }

    // A dwindle tile resized by resizeactive or resizewindowpixel, and the tiles that make room for it, move at once
    // unless misc:animate_manual_resizes is on, as the pinned release's dwindle resize warps them. Master tiles and
    // floating Clients animate, as there.
    private bool InstantResize(Outcome outcome, State before)
    {
        if (_session.AnimateManualResizes || outcome.Dispatcher is not ("resizeactive" or "resizewindowpixel"))
            return false;

        if (ActiveWindow() is not WindowId active)
            return false;

        foreach (var client in before.Clients)
        {
            if (client.Window != active)
                continue;

            return !client.Floating
                && FindWorkspace(client.Workspace) is LiveWorkspace desk
                && desk.Layout is DwindleLayout;
        }

        return false;
    }

    // A window's own animation style, from setprop or a window rule, replaces the windowsIn or windowsOut style it opens
    // or closes with, as in the pinned release. Its curve and speed stay the node's.
    private string WindowStyleOf(WindowId window, string node) =>
        OwnProp(window, "animation") is string { Length: > 0 } own ? own : _animations.StyleOf(node);

    // A Workspace's own animation style, from the animation: field of the workspace rule that matches it, or null.
    private string? WorkspaceStyleOf(WorkspaceId workspace) =>
        FindWorkspace(workspace) is LiveWorkspace desk ? RuleFor(desk)?.Animation : null;

    private Rectangle MonitorRectangle(WorkspaceId workspace)
    {
        if (FindWorkspace(workspace) is LiveWorkspace desk)
        {
            foreach (var screen in _monitors)
            {
                if (screen.Id == desk.Monitor)
                    return screen.Bounds;
            }
        }

        return _monitors.Count > 0 ? _monitors[0].Bounds : _area;
    }

    private sealed class MotionPlan
    {
        private readonly Core _core;
        private readonly State _before;
        private readonly State _after;
        private readonly Dictionary<WindowId, Client> _was = [];
        private readonly HashSet<WindowId> _wasHidden = [];
        private readonly Dictionary<WindowId, Client> _is = [];
        private readonly HashSet<WindowId> _isHidden = [];
        private readonly Dictionary<WindowId, Rectangle> _placed = [];
        private readonly Dictionary<WindowId, Motion> _motions = [];
        private readonly (WindowId Window, Rectangle Bounds)? _released;

        public MotionPlan(Core core, State before, State after, Outcome outcome, (WindowId Window, Rectangle Bounds)? released)
        {
            _core = core;
            _released = released;
            _before = before;
            _after = after;
            foreach (var client in before.Clients)
                _was[client.Window] = client;
            foreach (var client in before.Hidden)
                _wasHidden.Add(client.Window);
            var ids = new Dictionary<ClientId, WindowId>(after.Clients.Count);
            foreach (var client in after.Clients)
            {
                _is[client.Window] = client;
                ids[client.Id] = client.Window;
            }

            foreach (var client in after.Hidden)
                _isHidden.Add(client.Window);
            foreach (var placement in outcome.Placements)
            {
                if (ids.TryGetValue(placement.Client, out var window))
                    _placed[window] = placement.Bounds;
            }
        }

        public int Count => _motions.Count;

        private AnimationOptions Options => _core._animations;

        public void Workspaces(IReadOnlyList<WindowId> windows, bool entering)
        {
            foreach (var window in windows)
            {
                var source = entering ? _is : _was;
                if (!source.TryGetValue(window, out var client) || _motions.ContainsKey(window))
                    continue;

                // A window that was already shown is not entering, and one already hidden is not leaving.
                var wasShown = _was.ContainsKey(window) && !_wasHidden.Contains(window);
                if (entering == wasShown)
                    continue;

                if (!TrySwitch(client.Workspace, entering, out var leaf, out var left, out var styled))
                    continue;

                var timing = Options.TimingOf(leaf);
                if (timing is null)
                    continue;

                var monitor = _core.MonitorRectangle(client.Workspace);
                var between = _core.FindWorkspace(client.Workspace)?.Options.GapsWorkspaces ?? 0;
                var style = _core.WorkspaceStyleOf(styled) ?? Options.StyleOf(leaf);
                var shift = MotionStyles.Workspace(style, entering, left, client.Workspace.IsSpecial, monitor, between);
                var rest = _placed.TryGetValue(window, out var placed) ? placed : client.Bounds;
                var away = rest with { X = rest.X + shift.Dx, Y = rest.Y + shift.Dy };
                var moves = shift.Dx != 0 || shift.Dy != 0;
                _motions[window] = entering
                    ? new Motion(window, rest)
                    {
                        // A window still leaving turns back from where it is.
                        From = moves ? away : null,
                        Move = moves ? timing : null,
                        FadeFrom = shift.Fades ? 0 : null,
                        Fade = shift.Fades ? timing : null,
                    }
                    : new Motion(window, moves ? away : rest)
                    {
                        From = rest,
                        Move = moves ? timing : null,
                        FadeTo = shift.Fades ? 0 : 1,
                        Fade = shift.Fades ? timing : null,
                        End = MotionEnd.Hide,
                        Rest = rest,
                    };
            }
        }

        public void Close(WindowId window)
        {
            if (!_is.TryGetValue(window, out var client) || _isHidden.Contains(window) || client.Fullscreen == FullscreenMode.Fullscreen)
                return;

            var move = Options.TimingOf("windowsOut");
            var fade = Options.TimingOf("fadeOut");
            if (move is null && fade is null)
                return;

            var rest = client.Bounds;
            var to = move is null ? rest : MotionStyles.Window(_core.WindowStyleOf(window, "windowsOut"), rest, _core.MonitorRectangle(client.Workspace));
            _motions[window] = new Motion(window, to)
            {
                From = rest,
                Move = move,
                FadeTo = fade is null ? 1 : 0,
                Fade = fade,
                End = MotionEnd.Close,
                Rest = rest,
            };
        }

        public void Place(Client client, Rectangle bounds)
        {
            var window = client.Window;
            if (_motions.ContainsKey(window) || _isHidden.Contains(window))
                return;

            if (!_was.TryGetValue(window, out var prior))
            {
                if (client.Fullscreen != FullscreenMode.Fullscreen)
                    Open(client, bounds);
                return;
            }

            // A Client going into or out of fullscreen mode 2 moves between its rectangle and the monitor's, as in the
            // pinned release. One that stays fullscreen is put in place at once.
            var fullscreen = client.Fullscreen == FullscreenMode.Fullscreen;
            if (fullscreen && prior.Fullscreen == FullscreenMode.Fullscreen)
                return;

            // A window the system just moved starts from where it was dropped, not from the tile it was lifted out of.
            var from = _released is { } dropped && dropped.Window == window ? dropped.Bounds : prior.Bounds;
            if (_wasHidden.Contains(window) || from == bounds)
                return;

            if (Options.TimingOf("windowsMove") is Timing move)
                _motions[window] = new Motion(window, bounds) { From = from, Move = move };
        }

        // Alpha and border changes of a window that stays shown.
        public void Switches(Dictionary<WindowId, Decoration> previous, Dictionary<WindowId, Decoration> painted)
        {
            var alphaTiming = Options.TimingOf("fadeSwitch");
            var dimTiming = Options.TimingOf("fadeDim");
            var borderTiming = Options.TimingOf("border");
            if (alphaTiming is null && dimTiming is null && borderTiming is null)
                return;

            foreach (var (window, now) in painted)
            {
                if (!previous.TryGetValue(window, out var was) || _isHidden.Contains(window) || _wasHidden.Contains(window) || !_was.ContainsKey(window))
                    continue;

                var alpha = alphaTiming is not null && was.Opacity != now.Opacity;
                var dim = dimTiming is not null && was.Keep != now.Keep;
                var border = borderTiming is not null && was.Border != now.Border && !was.Borderless && !now.Borderless;
                if (!alpha && !dim && !border)
                    continue;

                var motion = _motions.TryGetValue(window, out var planned) ? planned : new Motion(window, BoundsOf(window));
                if (alpha)
                    motion = motion with { AlphaFrom = was.Opacity, Alpha = alphaTiming };
                if (dim)
                    motion = motion with { DimFrom = was.Keep, Dim = dimTiming };
                if (border)
                    motion = motion with { BorderFrom = was.Border, Border = borderTiming };
                _motions[window] = motion;
            }
        }

        public Motion[] Finish(Dictionary<WindowId, Decoration> painted)
        {
            var motions = new Motion[_motions.Count];
            var i = 0;
            foreach (var motion in _motions.Values)
            {
                motions[i++] = painted.TryGetValue(motion.Window, out var decoration)
                    ? motion with { AlphaTo = decoration.Alpha, OpacityTo = decoration.Opacity, DimTo = decoration.Keep, BorderTo = decoration.Borderless ? null : decoration.Border }
                    : motion;
            }

            return motions;
        }

        private void Open(Client client, Rectangle bounds)
        {
            var move = Options.TimingOf("windowsIn");
            var fade = Options.TimingOf("fadeIn");
            if (move is null && fade is null)
                return;

            var from = MotionStyles.Window(_core.WindowStyleOf(client.Window, "windowsIn"), bounds, _core.MonitorRectangle(client.Workspace));
            _motions[client.Window] = new Motion(client.Window, bounds)
            {
                From = move is null ? null : from,
                Warp = move is not null,
                Move = move,
                FadeFrom = fade is null ? null : 0,
                Fade = fade,
            };
        }

        private Rectangle BoundsOf(WindowId window)
        {
            if (_placed.TryGetValue(window, out var placed))
                return placed;

            return _is.TryGetValue(window, out var client) ? client.Bounds : default;
        }

        // The Workspace whose rule style the motion plays, as in the pinned release: the one switched to, for both the
        // Workspace leaving and the one entering, and for a special workspace that hides, the special workspace that
        // replaces it on its Monitor, or itself.
        private bool TrySwitch(WorkspaceId workspace, bool entering, out string leaf, out bool left, out WorkspaceId styled)
        {
            leaf = "";
            left = entering;
            styled = workspace;
            if (workspace.IsSpecial)
            {
                var wasShown = SpecialShown(_before, workspace);
                var isShown = SpecialShown(_after, workspace);
                if (entering ? wasShown || !isShown : !wasShown || isShown)
                    return false;

                if (!entering && Replacing(workspace) is WorkspaceId replacing)
                    styled = replacing;

                leaf = entering ? "specialWorkspaceIn" : "specialWorkspaceOut";
                return true;
            }

            WorkspaceId older;
            WorkspaceId newer;
            if (_before.Screens.Count == 0 || _after.Screens.Count == 0)
            {
                older = _before.ActiveWorkspace;
                newer = _after.ActiveWorkspace;
                if ((entering ? newer : older) != workspace)
                    return false;
            }
            else
            {
                var showing = (entering ? _after : _before).Screens.FirstOrDefault(screen => screen.ActiveWorkspace == workspace);
                var other = showing is null ? null : (entering ? _before : _after).Screens.FirstOrDefault(screen => screen.Id == showing.Id);
                if (other is null)
                    return false;

                older = entering ? other.ActiveWorkspace : workspace;
                newer = entering ? workspace : other.ActiveWorkspace;
            }

            if (older == newer)
                return false;

            leaf = entering ? "workspacesIn" : "workspacesOut";
            left = Wraps(newer, older) ^ (Compare(newer, older) > 0);
            styled = newer;
            return true;
        }

        // The special workspace shown in place of one that hides, on the same Monitor, or null.
        private WorkspaceId? Replacing(WorkspaceId special)
        {
            if (_before.Screens.Count == 0 || _after.Screens.Count == 0)
                return _after.SpecialWorkspace;

            var screen = _before.Screens.FirstOrDefault(candidate => candidate.SpecialWorkspace == special);
            return screen is null ? null : _after.Screens.FirstOrDefault(candidate => candidate.Id == screen.Id)?.SpecialWorkspace;
        }

        private static bool SpecialShown(State state, WorkspaceId workspace)
        {
            if (state.Screens.Count == 0)
                return state.SpecialWorkspace == workspace;

            foreach (var screen in state.Screens)
            {
                if (screen.SpecialWorkspace == workspace)
                    return true;
            }

            return false;
        }

        // With workspace_wraparound, the lowest and highest numbered workspaces animate as neighbours.
        private bool Wraps(WorkspaceId first, WorkspaceId second)
        {
            if (!Options.Wraparound || !first.TryGetNumber(out var a) || !second.TryGetNumber(out var b))
                return false;

            var lowest = int.MaxValue;
            var highest = int.MinValue;
            foreach (var workspace in _before.Workspaces.Concat(_after.Workspaces))
            {
                if (!workspace.Id.TryGetNumber(out var number))
                    continue;

                lowest = Math.Min(lowest, number);
                highest = Math.Max(highest, number);
            }

            return Math.Min(a, b) == lowest && Math.Max(a, b) == highest;
        }

        // Named workspaces sort below numbered ones, as their identifiers are negative.
        private static int Compare(WorkspaceId first, WorkspaceId second)
        {
            var numbered = first.TryGetNumber(out var a);
            var other = second.TryGetNumber(out var b);
            if (numbered && other)
                return a.CompareTo(b);
            if (numbered != other)
                return numbered ? 1 : -1;

            return string.CompareOrdinal(first.Name, second.Name);
        }
    }
}
