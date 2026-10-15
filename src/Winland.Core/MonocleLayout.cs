using System.Globalization;

namespace Winland;

/// <summary>
/// Shows one tiled Client at a time, sized to the whole tiling area.
/// The others stay on the Workspace and are left out of the placements, so the caller can conceal them.
/// </summary>
internal sealed class MonocleLayout : Layout
{
    private readonly List<Client> _clients = [];
    private readonly List<ClientId> _tiled = [];
    private readonly Dictionary<ClientId, Rectangle> _floating = [];
    private readonly Dictionary<ClientId, Rectangle> _natural = [];
    private readonly Dictionary<ClientId, (Size? Min, Size? Max)> _limits = [];
    private LayoutOptions _options = LayoutOptions.Unconfigured;
    private Rectangle _area;
    private int _visible;

    public IReadOnlyList<Client> Clients => _clients;

    public bool FollowsMonitorWhenUnmoved => true;

    public void NudgeFloating(int dx, int dy) => FloatingShift.By(_floating, dx, dy);

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
        Size? maxSize = null)
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
        _limits[id] = (minSize, maxSize);
        if (floating)
        {
            _clients.Add(new Client(id, window, Floating: true, Workspace: workspace, Class: windowClass, Title: title, ProcessId: processId, InitialClass: initialClass, InitialTitle: initialTitle));
            _floating[id] = bounds;
            return [new Placement(id, bounds)];
        }

        _clients.Add(new Client(id, window, Workspace: workspace, Class: windowClass, Title: title, ProcessId: processId, InitialClass: initialClass, InitialTitle: initialTitle));
        _tiled.Add(id);
        _visible = _tiled.Count - 1;
        return Placed();
    }

    public IReadOnlyList<Placement> Remove(WindowId window)
    {
        var id = new ClientId(window.Value);
        var index = IndexOf(id);
        if (index < 0)
            return [];

        var tiled = DropTiled(id);
        _floating.Remove(id);
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

        if (_floating.ContainsKey(id))
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
        if (_floating.TryGetValue(id, out var bounds))
            return [new Placement(id, bounds)];

        _tiled.Add(id);
        _visible = _tiled.Count - 1;
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
        var bounds = _floating.TryGetValue(id, out var floating)
            ? floating
            : _natural.TryGetValue(id, out var natural) ? natural : default;
        _limits.TryGetValue(id, out var limits);
        taken = new Taken(
            client.Floating,
            client.Minimized,
            bounds,
            client.Class,
            client.Title,
            client.ProcessId,
            client.InitialClass,
            client.InitialTitle,
            limits.Min,
            limits.Max);
        Remove(window);
        return true;
    }

    public IReadOnlyList<Placement> Visible()
    {
        var placements = new List<Placement>(Placed());
        foreach (var client in _clients)
        {
            if (!client.Floating || client.Minimized)
                continue;

            if (_floating.TryGetValue(client.Id, out var bounds))
                placements.Add(new Placement(client.Id, bounds));
        }

        return placements;
    }

    // Every tiled Client fills the area, so size limits do not change the placement here.
    // They are kept so a later layout can still honor them.
    public IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize)
    {
        var id = new ClientId(window.Value);
        if (IndexOf(id) < 0)
            return [];

        _limits[id] = (minSize, maxSize);
        return [];
    }

    public bool TryBounds(WindowId window, out Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        if (_floating.TryGetValue(id, out bounds))
            return true;

        if (_tiled.Contains(id))
        {
            bounds = Tile();
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

        return [new Placement(id, Tile())];
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

    public void NoteFocus(WindowId window)
    {
        var index = _tiled.IndexOf(new ClientId(window.Value));
        if (index >= 0)
            _visible = index;
    }

    public IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments)
    {
        var id = new ClientId(window.Value);
        if (Minimized(id) || !_floating.TryGetValue(id, out var box))
            return [];

        if (!TrySide(First(arguments), out var side))
            return [];

        var moved = side switch
        {
            Side.Left => box with { X = _area.X },
            Side.Right => box with { X = _area.X + _area.Width - box.Width },
            Side.Up => box with { Y = _area.Y },
            Side.Down => box with { Y = _area.Y + _area.Height - box.Height },
            _ => box,
        };

        if (moved == box)
            return [];

        _floating[id] = moved;
        return [new Placement(id, moved)];
    }

    public IReadOnlyList<Placement> ResizeActive(WindowId window, string arguments)
    {
        var id = new ClientId(window.Value);
        if (Minimized(id) || !_floating.TryGetValue(id, out var box))
            return [];

        if (!TryResize(arguments, out var x, out var y, out var exact))
            return [];

        var deltaX = exact ? x - box.Width : x;
        var deltaY = exact ? y - box.Height : y;
        var resized = new Rectangle(box.X - (deltaX / 2), box.Y - (deltaY / 2), box.Width + deltaX, box.Height + deltaY);
        if (resized.Width < 1 || resized.Height < 1)
            return [];

        _floating[id] = resized;
        return [new Placement(id, resized)];
    }

    public IReadOnlyList<Placement> ToggleFloating(WindowId window)
    {
        var id = new ClientId(window.Value);
        var index = IndexOf(id);
        if (index < 0 || _clients[index].Minimized)
            return [];

        if (_floating.ContainsKey(id))
        {
            _floating.Remove(id);
            SetFloating(id, false);
            _tiled.Add(id);
            _visible = _tiled.Count - 1;
            return Placed();
        }

        if (!DropTiled(id))
            return [];

        var floated = FloatingBounds(id, Tile());
        _floating[id] = floated;
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

    public IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag)
    {
        _ = window;
        _ = drag;
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
        if (_tiled.Count == 0)
        {
            _visible = 0;
            return true;
        }

        if (shown)
            _visible = index % _tiled.Count;
        else if (index < _visible)
            _visible--;

        return true;
    }

    private ClientId? VisibleTiled() =>
        _tiled.Count == 0 || _visible < 0 || _visible >= _tiled.Count ? null : _tiled[_visible];

    private Rectangle Tile()
    {
        var gaps = _options.GapsOut;
        var width = Math.Max(0, _area.Width - gaps.Left - gaps.Right);
        var height = Math.Max(0, _area.Height - gaps.Top - gaps.Bottom);
        var box = new Rectangle(_area.X + gaps.Left, _area.Y + gaps.Top, width, height);
        var border = _options.BorderSize;
        return Shrink(box, border, border, border, border);
    }

    private Rectangle FloatingBounds(ClientId id, Rectangle tile)
    {
        var width = 640;
        var height = 400;
        if (_natural.TryGetValue(id, out var natural) && natural.Width >= 5 && natural.Height >= 5)
        {
            width = natural.Width;
            height = natural.Height;
        }

        if (Math.Abs(width - tile.Width) < 5 && Math.Abs(height - tile.Height) < 5)
        {
            width += 10;
            height += 10;
        }

        var floated = new Rectangle(
            tile.X + (tile.Width / 2) - (width / 2),
            tile.Y + (tile.Height / 2) - (height / 2),
            width,
            height);
        return FitInWorkArea(floated, _options.FloatingWorkArea(_area));
    }

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        if (!_floating.ContainsKey(id) || bounds.Width < 1 || bounds.Height < 1)
            return [];

        _floating[id] = bounds;
        return Visible();
    }

    private static Rectangle FitInWorkArea(Rectangle box, Rectangle work)
    {
        var x = Math.Max(box.X, work.X);
        var y = Math.Max(box.Y, work.Y);
        if (x + box.Width > work.X + work.Width)
            x = work.X + work.Width - box.Width;
        if (y + box.Height > work.Y + work.Height)
            y = work.Y + work.Height - box.Height;
        return new Rectangle(x, y, box.Width, box.Height);
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

    private static bool TryResize(string arguments, out int x, out int y, out bool exact)
    {
        x = 0;
        y = 0;
        exact = false;
        var tokens = Tokens(arguments);
        var index = 0;
        if (tokens.Length > 0 && Is(tokens[0], "exact"))
        {
            exact = true;
            index = 1;
        }

        if (tokens.Length - index < 2)
            return false;

        return int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
            && int.TryParse(tokens[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
    }

    private static string First(string arguments)
    {
        var tokens = Tokens(arguments);
        return tokens.Length == 0 ? "" : tokens[0];
    }

    private static string[] Tokens(string arguments) =>
        arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static bool Is(string text, string expected) =>
        text.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private enum Side
    {
        Left,
        Right,
        Up,
        Down,
    }
}
