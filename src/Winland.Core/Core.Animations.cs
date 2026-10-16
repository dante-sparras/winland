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
            return outcome;

        var plan = new MotionPlan(this, before, after, outcome, released);
        plan.Workspaces(outcome.Hide, entering: false);
        plan.Workspaces(outcome.Show, entering: true);
        foreach (var closing in outcome.Closes)
            plan.Close(closing);

        if (_gesture is null && _border is null && _nativeSize is null)
        {
            foreach (var placement in outcome.Placements)
            {
                if (windows.TryGetValue(placement.Client, out var client))
                    plan.Place(client, placement.Bounds);
            }
        }

        plan.Switches(previous, _painted);
        return plan.Count == 0 ? outcome : outcome with { Motions = plan.Finish(_painted) };
    }

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

                if (!TrySwitch(client.Workspace, entering, out var leaf, out var left))
                    continue;

                var timing = Options.TimingOf(leaf);
                if (timing is null)
                    continue;

                var monitor = _core.MonitorRectangle(client.Workspace);
                var shift = MotionStyles.Workspace(Options.StyleOf(leaf), entering, left, client.Workspace.IsSpecial, monitor);
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
            var to = move is null ? rest : MotionStyles.Window(Options.StyleOf("windowsOut"), rest, _core.MonitorRectangle(client.Workspace));
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
            if (_motions.ContainsKey(window) || _isHidden.Contains(window) || client.Fullscreen == FullscreenMode.Fullscreen)
                return;

            if (!_was.TryGetValue(window, out var prior))
            {
                Open(client, bounds);
                return;
            }

            // A window the system just moved starts from where it was dropped, not from the tile it was lifted out of.
            var from = _released is { } dropped && dropped.Window == window ? dropped.Bounds : prior.Bounds;
            if (_wasHidden.Contains(window) || from == bounds || prior.Fullscreen == FullscreenMode.Fullscreen)
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
                if (!previous.TryGetValue(window, out var was) || _isHidden.Contains(window) || _wasHidden.Contains(window) || !_was.ContainsKey(window))
                    continue;

                var alpha = alphaTiming is not null && was.Alpha != now.Alpha;
                var border = borderTiming is not null && was.Border != now.Border && !was.Borderless && !now.Borderless;
                if (!alpha && !border)
                    continue;

                var motion = _motions.TryGetValue(window, out var planned) ? planned : new Motion(window, BoundsOf(window));
                if (alpha)
                    motion = motion with { AlphaFrom = was.Alpha, Alpha = alphaTiming };
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

            var from = MotionStyles.Window(Options.StyleOf("windowsIn"), bounds, _core.MonitorRectangle(client.Workspace));
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

        private bool TrySwitch(WorkspaceId workspace, bool entering, out string leaf, out bool left)
        {
            leaf = "";
            left = entering;
            if (workspace.IsSpecial)
            {
                var wasShown = SpecialShown(_before, workspace);
                var isShown = SpecialShown(_after, workspace);
                if (entering ? wasShown || !isShown : !wasShown || isShown)
                    return false;

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
            return true;
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
