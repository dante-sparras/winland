using static Winland.OrdinalText;

namespace Winland;

/// <summary>
/// Shows one tiled Client at a time, sized to the whole tiling area.
/// The others stay on the Workspace and are left out of the placements, so the caller can conceal them.
/// </summary>
internal sealed partial class MonocleLayout : Layout
{
    private readonly List<Client> _clients = [];
    private readonly List<ClientId> _tiled = [];
    private readonly FloatingClients _floating = new();
    private readonly Dictionary<ClientId, Rectangle> _natural = [];
    private readonly Dictionary<ClientId, SizeLimits> _limits = [];
    private LayoutOptions _options = LayoutOptions.Unconfigured;
    private Rectangle _area;
    private int _visible;

    // The tiled Clients by how recently they were focused, the newest first.
    private readonly List<ClientId> _recent = [];

    public IReadOnlyList<Client> Clients => _clients;

    public void Retitle(WindowId window, string title)
    {
        for (var i = 0; i < _clients.Count; i++)
        {
            if (_clients[i].Window == window)
                _clients[i] = _clients[i] with { Title = title };
        }
    }

    public void Reclass(WindowId window, string windowClass)
    {
        for (var i = 0; i < _clients.Count; i++)
        {
            if (_clients[i].Window == window)
                _clients[i] = _clients[i] with { Class = windowClass };
        }
    }

    public void Readdress(WorkspaceId workspace)
    {
        for (var i = 0; i < _clients.Count; i++)
            _clients[i] = _clients[i] with { Workspace = workspace };
    }

    public bool FollowsMonitorWhenUnmoved => true;

    public void NudgeFloating(int dx, int dy) => _floating.Nudge(dx, dy);

    public string? LayoutMessageError { get; private set; }

    public IReadOnlyList<Placement> Apply(LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        return Placed();
    }

    public void Fit(Rectangle area) => _area = area;

    public IReadOnlyList<Placement> Add(
        WindowId window,
        Rectangle area,
        Point? pointer,
        bool floating,
        Rectangle bounds,
        WorkspaceId workspace,
        string windowClass = "",
        string title = "",
        bool activeWorkspace = true,
        Rectangle monitor = default,
        long processId = 0,
        string initialClass = "",
        string initialTitle = "",
        Size? minSize = null,
        Size? maxSize = null,
        TiledLimits? tiledLimits = null)
    {
        _ = pointer;
        _ = minSize;
        _ = maxSize;
        _ = activeWorkspace;
        _ = monitor;
        _area = area;
        var id = new ClientId(window.Value);
        if (_clients.Exists(client => client.Id == id))
            return [];

        _natural[id] = bounds;
        _limits[id] = new SizeLimits(minSize, maxSize) { Tiled = tiledLimits };
        var placed = new SizeLimits(minSize, maxSize).Clamp(bounds);
        _floating.Remember(id, placed);

        _clients.Add(ClientAdoption.Adopted(id, window, workspace, windowClass, title, floating, processId, initialClass, initialTitle));
        if (floating)
        {
            _floating.Set(id, placed);
            return [new Placement(id, placed)];
        }

        _tiled.Add(id);
        _visible = _tiled.Count - 1;
        _recent.Remove(id);
        _recent.Insert(0, id);
        return Placed();
    }

    public IReadOnlyList<Placement> Remove(WindowId window)
    {
        var id = new ClientId(window.Value);
        var index = IndexOf(id);
        if (index < 0)
            return [];

        var tiled = DropTiled(id);
        _ = _drag.Forget(id);
        _floating.Forget(id);
        _natural.Remove(id);
        _limits.Remove(id);
        _clients.RemoveAt(index);
        return tiled ? Placed() : [];
    }

    public IReadOnlyList<Placement> Minimize(WindowId window)
    {
        var id = new ClientId(window.Value);
        if (IndexOf(id) < 0)
            return [];

        if (_floating.Contains(id))
        {
            SetMinimized(id, true);
            return [];
        }

        if (!DropTiled(id))
            return [];

        SetMinimized(id, true);
        return Placed();
    }

    public IReadOnlyList<Placement> Restore(WindowId window)
    {
        var id = new ClientId(window.Value);
        var index = IndexOf(id);
        if (index < 0 || !_clients[index].Minimized)
            return [];

        SetMinimized(id, false);
        if (_floating.TryGet(id, out var bounds))
            return [new Placement(id, bounds)];

        _tiled.Add(id);
        _visible = _tiled.Count - 1;
        _recent.Remove(id);
        _recent.Insert(0, id);
        return Placed();
    }

    public bool TryTake(WindowId window, out Taken taken)
    {
        taken = default;
        var id = new ClientId(window.Value);
        var index = IndexOf(id);
        if (index < 0)
            return false;

        var client = _clients[index];
        var bounds = _floating.TryGet(id, out var floating)
            ? floating
            : _natural.TryGetValue(id, out var natural) ? natural : default;
        _limits.TryGetValue(id, out var limits);
        taken = SlotHandover.Describe(client, bounds, limits);
        Remove(window);
        return true;
    }

    public IReadOnlyList<Placement> Visible()
    {
        var placements = new List<Placement>(Placed());
        _floating.AddShown(placements, _clients);
        return placements;
    }

    // Every tiled Client fills the area, so size limits do not change a tiled placement here.
    // They are kept so a later layout can still honor them. A floating Client is clamped to them.
    public IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize, TiledLimits? tiledLimits = null)
    {
        var id = new ClientId(window.Value);
        if (IndexOf(id) < 0)
            return [];

        _limits[id] = new SizeLimits(minSize, maxSize) { Tiled = tiledLimits };
        return _floating.Clamp(id, new SizeLimits(minSize, maxSize));
    }

    public bool TryBounds(WindowId window, out Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        if (_floating.TryGet(id, out bounds))
            return true;

        if (_tiled.Contains(id))
        {
            bounds = Tile(id);
            return true;
        }

        bounds = default;
        return false;
    }

    public IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode)
    {
        if (!FullscreenClients.Assign(_clients, window, mode, clientMode))
            return [];

        if (mode != FullscreenMode.None)
        {
            var index = _tiled.IndexOf(new ClientId(window.Value));
            if (index >= 0)
                _visible = index;
        }

        return Visible();
    }

    public IReadOnlyList<Placement> Placed()
    {
        if (VisibleTiled() is not ClientId id)
            return [];

        return [new Placement(id, Tile(id))];
    }

    public IReadOnlyList<Placement> Preselect(string direction)
    {
        ArgumentNullException.ThrowIfNull(direction);
        return [];
    }

    public IReadOnlyList<Placement> TogglePseudo(WindowId window)
    {
        _ = window;
        return [];
    }

    public WindowId? MoveFocus(WindowId active, string arguments)
    {
        if (!TrySide(First(arguments), out var side))
            return null;

        if (_tiled.Count == 0)
            return null;

        if (_tiled.Count == 1)
        {
            var only = Window(_tiled[0]);
            return only == active ? null : only;
        }

        Cycle(Forward(side) ? 1 : -1);
        return Window(_tiled[_visible]);
    }

    public WindowId? Neighbor(WindowId active, string arguments)
    {
        if (!TrySide(First(arguments), out var side) || _tiled.Count == 0)
            return null;

        if (_tiled.Count == 1)
        {
            var only = Window(_tiled[0]);
            return only == active ? null : only;
        }

        var count = _tiled.Count;
        var delta = Forward(side) ? 1 : -1;
        return Window(_tiled[(((_visible + delta) % count) + count) % count]);
    }

    // The shown tile takes the focus when the focused Client closes, as the pinned release's monocle picks it.
    public WindowId? FocusAfterClose() => VisibleTiled() is ClientId shown ? Window(shown) : null;

    public void NoteFocus(WindowId window)
    {
        var id = new ClientId(window.Value);
        var index = _tiled.IndexOf(id);
        if (index >= 0)
            _visible = index;

        _recent.Remove(id);
        _recent.Insert(0, id);
    }

    public IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments)
    {
        var id = new ClientId(window.Value);
        if (Minimized(id) || !_floating.Contains(id))
            return [];

        if (!TrySide(First(arguments), out var side))
            return [];

        return _floating.MoveToEdge(id, side, _options.FloatingWorkArea(_area), _options.BorderFor(id));
    }

    public IReadOnlyList<Placement> ResizeActive(WindowId window, string arguments)
    {
        var id = new ClientId(window.Value);
        if (Minimized(id) || !_floating.Contains(id))
            return [];

        if (!ResizeArguments.TryParse(arguments, out var x, out var y, out var exact))
            return [];

        return _floating.Resize(id, x, y, exact, _options.FloatingOnScreen(_area));
    }

    public IReadOnlyList<Placement> ToggleFloating(WindowId window)
    {
        var id = new ClientId(window.Value);
        var index = IndexOf(id);
        if (index < 0 || _clients[index].Minimized)
            return [];

        if (_floating.Sink(id))
        {
            SetFloating(id, false);
            _tiled.Add(id);
            _visible = _tiled.Count - 1;
            return Placed();
        }

        if (!DropTiled(id))
            return [];

        var floated = _floating.Around(id, Tile(id), _options.FloatingOnScreen(_area));
        _floating.Set(id, floated);
        SetFloating(id, true);
        var placements = new List<Placement>(Placed()) { new(id, floated) };
        return placements;
    }

    public IReadOnlyList<Placement> LayoutMessage(WindowId? active, string arguments)
    {
        _ = active;
        LayoutMessageError = null;
        var tokens = Tokens(arguments);
        if (tokens.Length == 0)
        {
            LayoutMessageError = "error: bad layoutmsg";
            return [];
        }

        // cyclenext and cycleprev take no parameters. Trailing words, including tiled and floating, are ignored.
        if (Is(tokens[0], "cyclenext"))
        {
            Cycle(1);
            return Placed();
        }

        if (Is(tokens[0], "cycleprev"))
        {
            Cycle(-1);
            return Placed();
        }

        LayoutMessageError = "error: bad layoutmsg '" + tokens[0] + "'";
        return [];
    }

    private void Cycle(int delta)
    {
        if (_tiled.Count == 0)
            return;

        var count = _tiled.Count;
        _visible = (((_visible + delta) % count) + count) % count;
    }

    private bool DropTiled(ClientId id)
    {
        var index = _tiled.IndexOf(id);
        if (index < 0)
            return false;

        var shown = index == _visible;
        _tiled.RemoveAt(index);
        _recent.Remove(id);
        if (_tiled.Count == 0)
        {
            _visible = 0;
            return true;
        }

        // As in the pinned release, the Client focused most recently is shown in its place, else the next one.
        if (shown)
            _visible = MostRecentTiled() ?? index % _tiled.Count;
        else if (index < _visible)
            _visible--;

        return true;
    }

    private int? MostRecentTiled()
    {
        foreach (var id in _recent)
        {
            var index = _tiled.IndexOf(id);
            if (index >= 0)
                return index;
        }

        return null;
    }

    private ClientId? VisibleTiled() =>
        _tiled.Count == 0 || _visible < 0 || _visible >= _tiled.Count ? null : _tiled[_visible];

    private Rectangle Tile(ClientId? client = null)
    {
        var gaps = _options.GapsOut;
        var width = Math.Max(0, _area.Width - gaps.Left - gaps.Right);
        var height = Math.Max(0, _area.Height - gaps.Top - gaps.Bottom);
        var box = _options.KeepAspect(new Rectangle(_area.X + gaps.Left, _area.Y + gaps.Top, width, height), _area, _tiled.Count);
        var border = client is ClientId id ? _options.BorderFor(id) : _options.BorderSize;
        return Shrink(box, border, border, border, border);
    }

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        return _floating.Place(id, bounds, _options.FloatingOnScreen(_area)) ? Visible() : [];
    }

    private static Rectangle Shrink(Rectangle box, int left, int top, int right, int bottom)
    {
        var width = Math.Max(0, box.Width - left - right);
        var height = Math.Max(0, box.Height - top - bottom);
        return new Rectangle(box.X + left, box.Y + top, width, height);
    }

    private int IndexOf(ClientId id) => _clients.FindIndex(client => client.Id == id);

    private bool Minimized(ClientId id)
    {
        var index = IndexOf(id);
        return index >= 0 && _clients[index].Minimized;
    }

    private void SetMinimized(ClientId id, bool minimized)
    {
        var index = IndexOf(id);
        _clients[index] = _clients[index] with { Minimized = minimized };
    }

    private void SetFloating(ClientId id, bool floating)
    {
        var index = IndexOf(id);
        _clients[index] = _clients[index] with { Floating = floating };
    }

    private static WindowId Window(ClientId id) => new(id.Value);

    private static bool Forward(Side side) => side is Side.Right or Side.Down;

    private static bool TrySide(string text, out Side side)
    {
        if (Is(text, "l") || Is(text, "left"))
        {
            side = Side.Left;
            return true;
        }

        if (Is(text, "r") || Is(text, "right"))
        {
            side = Side.Right;
            return true;
        }

        if (Is(text, "u") || Is(text, "up") || Is(text, "t") || Is(text, "top"))
        {
            side = Side.Up;
            return true;
        }

        if (Is(text, "d") || Is(text, "down") || Is(text, "b") || Is(text, "bottom"))
        {
            side = Side.Down;
            return true;
        }

        side = default;
        return false;
    }

    private static string First(string arguments)
    {
        var tokens = Tokens(arguments);
        return tokens.Length == 0 ? "" : tokens[0];
    }

    private static string[] Tokens(string arguments) =>
        arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
}
