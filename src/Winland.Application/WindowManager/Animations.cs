namespace Winland.Application;

/// <summary>Plans the motions of an outcome: how each window gets from where it was to where it goes.</summary>
public sealed partial class WindowManager
{
    private readonly Dictionary<WindowId, Decoration> _painted = [];

    /// <summary>Reads the system's reduced motion setting. When it returns true, nothing animates.</summary>
    public Func<bool>? ReducedMotion { get; set; }

    // Plans the motions for an outcome by comparing it with the state before it. The placements,
    // shows and hides stay as they are; a motion only says how the adapter gets the window there.
    private Outcome Animate(Outcome outcome, StateChange change)
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

        if (!_config.Animations.Enabled || ReducedMotion?.Invoke() == true)
        {
            _swipeLanding = null;
            return outcome;
        }

        // A live swipe's motions are already planned, and win over anything planned here for their windows.
        var own = outcome.Motions;
        var plan = new MotionPlan(this, change, outcome, released);
        plan.Workspaces(outcome.Hide, entering: false);
        plan.Workspaces(outcome.Show, entering: true);
        foreach (var closing in outcome.Closes)
            plan.Close(closing);

        // A window the pointer drags follows it at once, and the windows its Layout moves to make room for it, when it
        // is lifted out or dropped, animate. A mouse resize moves every window at once.
        if (_interaction is not (Gesture { Resize: true } or BorderDrag or NativeSize) && !InstantResize(outcome, change.Before))
        {
            var held = (_interaction as Gesture)?.Window;
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

        // A window with no_anim moves, fades and changes border color at once, as Winland warps it.
        var planned = FromSwipe(plan.Finish(_painted)).Where(motion => _props.Own(motion.Window, WindowProp.NoAnim) != true).ToList();
        if (own.Count == 0)
            return outcome with { Motions = planned };

        var mine = new HashSet<WindowId>(own.Select(motion => motion.Window));
        return outcome with { Motions = [.. own, .. planned.Where(motion => !mine.Contains(motion.Window))] };
    }

    // A dwindle tile resized by resizeactive, and the tiles that make room for it, move at once
    // as Winland's dwindle resize warps them. Floating Clients animate.
    private bool InstantResize(Outcome outcome, State before)
    {
        if (!TraitsOf(outcome.Dispatcher).HasFlag(DispatcherTraits.InstantResize))
            return false;

        if (ActiveWindow() is not WindowId active)
            return false;

        foreach (var client in before.Clients)
        {
            if (client.Window != active)
                continue;

            return !client.Floating
                && _workspaces.Find(client.Workspace) is LiveWorkspace desk
                && desk.Layout is DwindleLayout;
        }

        return false;
    }

    // A window's own animation style, from setprop or a window rule, replaces the windowsIn or windowsOut style it opens
    // or closes with. Its curve and speed stay the node's.
    private string WindowStyleOf(WindowId window, string node) =>
        _props.Own(window, WindowProp.Animation) is { Length: > 0 } own ? own : _config.Animations.StyleOf(node);

    private Rectangle MonitorRectangle(WorkspaceId workspace)
    {
        if (_workspaces.Find(workspace) is LiveWorkspace desk)
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
        private readonly WindowManager _core;
        private readonly StateChange _change;
        private readonly Dictionary<WindowId, Rectangle> _placed = [];
        private readonly Dictionary<WindowId, Motion> _motions = [];
        private readonly (WindowId Window, Rectangle Bounds)? _released;

        public MotionPlan(WindowManager core, StateChange change, Outcome outcome, (WindowId Window, Rectangle Bounds)? released)
        {
            _core = core;
            _released = released;
            _change = change;
            var ids = new Dictionary<ClientId, WindowId>(change.After.Clients.Count);
            foreach (var client in change.After.Clients)
                ids[client.Id] = client.Window;

            foreach (var placement in outcome.Placements)
            {
                if (ids.TryGetValue(placement.Client, out var window))
                    _placed[window] = placement.Bounds;
            }
        }

        public int Count => _motions.Count;

        private AnimationOptions Options => _core._config.Animations;

        public void Workspaces(IReadOnlyList<WindowId> windows, bool entering)
        {
            foreach (var window in windows)
            {
                var source = entering ? _change.Is : _change.Was;
                if (!source.TryGetValue(window, out var client) || _motions.ContainsKey(window))
                    continue;

                // A window that was already shown is not entering, and one already hidden is not leaving.
                if (entering == _change.WasShown(window))
                    continue;

                if (!TrySwitch(client.Workspace, entering, out var leaf, out var left, out var styled))
                    continue;

                var timing = Options.TimingOf(leaf);
                if (timing is null)
                    continue;

                var monitor = _core.MonitorRectangle(client.Workspace);
                var style = Options.StyleOf(leaf);
                var shift = MotionStyles.Workspace(style, entering, left, client.Workspace.IsSpecial, monitor);
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
            if (!_change.Is.TryGetValue(window, out var client) || _change.HiddenAfter.Contains(window) || client.Fullscreen == FullscreenMode.Fullscreen)
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
            if (_motions.ContainsKey(window) || _change.HiddenAfter.Contains(window))
                return;

            if (!_change.Was.TryGetValue(window, out var prior))
            {
                if (client.Fullscreen != FullscreenMode.Fullscreen)
                    Open(client, bounds);
                return;
            }

            // A Client going into or out of fullscreen mode 2 moves between its rectangle and the monitor's, as in the
            // Winland. One that stays fullscreen is put in place at once.
            var fullscreen = client.Fullscreen == FullscreenMode.Fullscreen;
            if (fullscreen && prior.Fullscreen == FullscreenMode.Fullscreen)
                return;

            // A window the system just moved starts from where it was dropped, not from the tile it was lifted out of.
            var from = _released is { } dropped && dropped.Window == window ? dropped.Bounds : prior.Bounds;
            if (_change.HiddenBefore.Contains(window) || from == bounds)
                return;

            if (Options.TimingOf("windowsMove") is Timing move)
                _motions[window] = new Motion(window, bounds) { From = from, Move = move };
        }

        // Alpha and border changes of a window that stays shown.
        public void Switches(Dictionary<WindowId, Decoration> previous, Dictionary<WindowId, Decoration> painted)
        {
            var alphaTiming = Options.TimingOf("fadeSwitch");
            var borderTiming = Options.TimingOf("border");
            if (alphaTiming is null && borderTiming is null)
                return;

            foreach (var (window, now) in painted)
            {
                if (!previous.TryGetValue(window, out var was) || _change.HiddenAfter.Contains(window) || _change.HiddenBefore.Contains(window) || !_change.Was.ContainsKey(window))
                    continue;

                var alpha = alphaTiming is not null && was.Opacity != now.Opacity;
                var border = borderTiming is not null && was.Border != now.Border && !was.Borderless && !now.Borderless;
                if (!alpha && !border)
                    continue;

                var motion = _motions.TryGetValue(window, out var planned) ? planned : new Motion(window, BoundsOf(window));
                if (alpha)
                    motion = motion with { AlphaFrom = was.Opacity, Alpha = alphaTiming };
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
                    ? motion with { AlphaTo = decoration.Alpha, BorderTo = decoration.Borderless ? null : decoration.Border }
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

            return _change.Is.TryGetValue(window, out var client) ? client.Bounds : default;
        }

        // The Workspace whose rule style the motion plays: the one switched to, for both the
        // Workspace leaving and the one entering, and for a special workspace that hides, the special workspace that
        // replaces it on its Monitor, or itself.
        private bool TrySwitch(WorkspaceId workspace, bool entering, out string leaf, out bool left, out WorkspaceId styled)
        {
            leaf = "";
            left = entering;
            styled = workspace;
            if (workspace.IsSpecial)
            {
                var wasShown = SpecialShown(_change.Before, workspace);
                var isShown = SpecialShown(_change.After, workspace);
                if (entering ? wasShown || !isShown : !wasShown || isShown)
                    return false;

                if (!entering && Replacing(workspace) is WorkspaceId replacing)
                    styled = replacing;

                leaf = entering ? "specialWorkspaceIn" : "specialWorkspaceOut";
                return true;
            }

            WorkspaceId older;
            WorkspaceId newer;
            if (_change.Before.Screens.Count == 0 || _change.After.Screens.Count == 0)
            {
                older = _change.Before.ActiveWorkspace;
                newer = _change.After.ActiveWorkspace;
                if ((entering ? newer : older) != workspace)
                    return false;
            }
            else
            {
                var showing = (entering ? _change.After : _change.Before).Screens.FirstOrDefault(screen => screen.ActiveWorkspace == workspace);
                var other = showing is null ? null : (entering ? _change.Before : _change.After).Screens.FirstOrDefault(screen => screen.Id == showing.Id);
                if (other is null)
                    return false;

                older = entering ? other.ActiveWorkspace : workspace;
                newer = entering ? workspace : other.ActiveWorkspace;
            }

            if (older == newer)
                return false;

            leaf = entering ? "workspacesIn" : "workspacesOut";
            left = Compare(newer, older) > 0;
            styled = newer;
            return true;
        }

        // The special workspace shown in place of one that hides, on the same Monitor, or null.
        private WorkspaceId? Replacing(WorkspaceId special)
        {
            if (_change.Before.Screens.Count == 0 || _change.After.Screens.Count == 0)
                return _change.After.SpecialWorkspace;

            var screen = _change.Before.Screens.FirstOrDefault(candidate => candidate.SpecialWorkspace == special);
            return screen is null ? null : _change.After.Screens.FirstOrDefault(candidate => candidate.Id == screen.Id)?.SpecialWorkspace;
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
