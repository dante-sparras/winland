namespace Winland.Domain;

internal sealed partial class MasterLayout : Layout
{
    private readonly List<Client> _clients = [];
    private readonly List<Tile> _tiles = [];
    private readonly FloatingClients _floating = new();
    private readonly Dictionary<ClientId, Rectangle> _natural = [];
    private readonly PseudoTiles _pseudo = new();
    private readonly Dictionary<ClientId, SizeLimits> _limits = [];
    private readonly Dictionary<ClientId, Suspended> _suspended = [];
    private LayoutOptions _layout = LayoutOptions.Unconfigured;
    private Rectangle _monitor;
    private MasterOptions _master = MasterOptions.Documented;
    private MasterOrientation? _explicit;
    private WindowId? _focused;
    private Rectangle _area;

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

    public WindowId? LayoutMessageFocus { get; private set; }

    public IReadOnlyList<Placement> Apply(LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _layout = options;
        Recalculate();
        return Placed();
    }

    public void Apply(MasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _master = options;
        Recalculate();
    }

    public void Fit(Rectangle area)
    {
        _area = area;
        Recalculate();
    }

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
        _ = activeWorkspace;
        _monitor = monitor;
        _area = area;
        var id = Id(window);
        if (Known(id))
            return [];

        _limits[id] = new SizeLimits(minSize, maxSize) { Tiled = tiledLimits };
        _natural[id] = bounds;
        var placed = new SizeLimits(minSize, maxSize).Clamp(bounds);
        _floating.Remember(id, placed);
        var client = ClientAdoption.Adopted(id, window, workspace, windowClass, title, floating, processId, initialClass, initialTitle);
        if (floating)
        {
            _clients.Add(client);
            _floating.Set(id, placed);
            _focused = window;
            return [new Placement(id, placed)];
        }

        _clients.Add(client);
        var predicted = PredictedTile();
        var tile = new Tile { Id = id, MasterFraction = _master.Mfact };
        var tiled = Adopt(tile, firstMap: true, dragging: false, pointer: null);
        _focused = window;
        if (!tiled)
        {
            // It would have been sized for the tile it was given, so it floats at that size.
            if (predicted is Rectangle size)
                placed = FloatedLimits.Centered(new SizeLimits(minSize, maxSize), size, _area);

            MarkFloating(id, true);
            _floating.Set(id, placed);
            return [.. Placed(), new Placement(id, placed)];
        }

        return Placed();
    }

    public IReadOnlyList<Placement> Remove(WindowId window)
    {
        var id = Id(window);
        _natural.Remove(id);
        _pseudo.Forget(id);
        _limits.Remove(id);
        _drag.Forget(id);

        if (_floating.Forget(id) || _suspended.Remove(id))
        {
            _clients.RemoveAll(client => client.Id == id);
            ForgetFocus(window);
            return [];
        }

        var tile = Find(window);
        if (tile is null)
        {
            _clients.RemoveAll(client => client.Id == id);
            return [];
        }

        Detach(tile);
        _clients.RemoveAll(client => client.Id == id);
        ForgetFocus(window);
        return Placed();
    }

    public IReadOnlyList<Placement> Minimize(WindowId window)
    {
        var id = Id(window);
        if (_floating.Contains(id))
        {
            MarkMinimized(id, true);
            return [];
        }

        var tile = Find(window);
        if (tile is null)
            return [];

        var index = _tiles.IndexOf(tile);
        var masters = Masters();
        Detach(tile);
        var promoted = tile.Master ? _tiles.Find(other => other.Master && !masters.Contains(other)) : null;
        _suspended[id] = new Suspended(tile, index, promoted);
        MarkMinimized(id, true);
        return Placed();
    }

    public IReadOnlyList<Placement> Restore(WindowId window)
    {
        var id = Id(window);
        if (_floating.TryGet(id, out var bounds))
        {
            if (!Minimized(id))
                return [];

            MarkMinimized(id, false);
            return [new Placement(id, bounds)];
        }

        if (!_suspended.Remove(id, out var saved))
            return [];

        MarkMinimized(id, false);

        // A master that was minimized takes its place back from the tile promoted in its stead.
        if (saved.Tile.Master && saved.Promoted is { Master: true } promoted && _tiles.Contains(promoted))
            promoted.Master = false;

        var index = Math.Clamp(saved.Index, 0, _tiles.Count);
        _tiles.Insert(index, saved.Tile);
        if (MasterCount() == 0)
            saved.Tile.Master = true;

        Recalculate();
        _focused = window;
        return Placed();
    }

    public bool TryTake(WindowId window, out Taken taken)
    {
        taken = default;
        var id = Id(window);
        var index = _clients.FindIndex(client => client.Id == id);
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

    // A maximum is remembered and handed on with the Client. It clamps a floating window.
    // It does not shrink a tiled master or slave: the master fraction owns that size.
    public IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize, TiledLimits? tiledLimits = null)
    {
        var id = Id(window);
        if (FindClient(id) < 0)
            return [];

        _limits[id] = new SizeLimits(minSize, maxSize) { Tiled = tiledLimits };
        if (_floating.Contains(id))
            return _floating.Clamp(id, new SizeLimits(minSize, maxSize));

        if (Find(window) is null && !_suspended.ContainsKey(id))
            return [];

        Recalculate();
        return Placed();
    }

    public bool TryBounds(WindowId window, out Rectangle bounds)
    {
        var id = Id(window);
        if (_floating.TryGet(id, out bounds))
            return true;

        var tile = Find(window);
        if (tile is not null)
        {
            bounds = Present(tile);
            return true;
        }

        if (_suspended.TryGetValue(id, out var suspended))
        {
            bounds = suspended.Tile.Box;
            return true;
        }

        bounds = default;
        return false;
    }

    // Every tiled Client is placed, so master does not conceal one behind another.
    public IReadOnlyList<Placement> Visible()
    {
        var placements = new List<Placement>(Placed());
        _floating.AddShown(placements, _clients);
        return placements;
    }

    public IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode)
    {
        if (!FullscreenClients.Assign(_clients, window, mode, clientMode))
            return [];

        return Visible();
    }

    public IReadOnlyList<Placement> Placed()
    {
        var placements = new List<Placement>();
        foreach (var client in _clients)
        {
            if (client.Minimized || client.Floating)
                continue;

            var tile = Find(client.Window);
            if (tile is not null)
                placements.Add(new Placement(client.Id, Present(tile)));
        }

        return placements;
    }

    public IReadOnlyList<Placement> Preselect(string direction)
    {
        ArgumentNullException.ThrowIfNull(direction);
        return [];
    }

    public IReadOnlyList<Placement> TogglePseudo(WindowId window)
    {
        var id = Id(window);
        if (FindClient(id) < 0)
            return [];

        _pseudo.Toggle(id);
        return Placed();
    }

    public WindowId? FocusAfterClose()
    {
        if (!_master.FocusMasterOnClose)
            return null;

        var master = FirstMaster();
        return master is null ? null : WindowOf(master);
    }

    private void ForgetFocus(WindowId window)
    {
        if (_focused != window)
            return;

        _focused = FocusAfterClose() ?? LastOpen();
    }

    private WindowId? LastOpen()
    {
        for (var i = _clients.Count - 1; i >= 0; i--)
        {
            if (!_clients[i].Minimized)
                return _clients[i].Window;
        }

        return null;
    }

    private Tile? Closest(Point point)
    {
        Tile? best = null;
        var bestDistance = double.MaxValue;
        foreach (var tile in _tiles)
        {
            var distance = DistanceSquared(tile.Box, point);
            if (distance < bestDistance)
            {
                best = tile;
                bestDistance = distance;
            }
        }

        return best;
    }

    private List<Tile> Masters()
    {
        var masters = new List<Tile>();
        foreach (var tile in _tiles)
        {
            if (tile.Master)
                masters.Add(tile);
        }

        return masters;
    }

    private List<Tile> Slaves()
    {
        var slaves = new List<Tile>();
        foreach (var tile in _tiles)
        {
            if (!tile.Master)
                slaves.Add(tile);
        }

        return slaves;
    }

    private Tile? FirstMaster()
    {
        foreach (var tile in _tiles)
        {
            if (tile.Master)
                return tile;
        }

        return null;
    }

    private Tile? FirstSlave()
    {
        foreach (var tile in _tiles)
        {
            if (!tile.Master)
                return tile;
        }

        return null;
    }

    private int MasterCount()
    {
        var count = 0;
        foreach (var tile in _tiles)
        {
            if (tile.Master)
                count++;
        }

        return count;
    }

    private int SlaveCount() => _tiles.Count - MasterCount();

    private Tile? Find(WindowId? window)
    {
        if (window is not WindowId id)
            return null;

        var client = Id(id);
        foreach (var tile in _tiles)
        {
            if (tile.Id == client)
                return tile;
        }

        return null;
    }

    private bool Known(ClientId id) =>
        FindClient(id) >= 0;

    private int FindClient(ClientId id) =>
        _clients.FindIndex(client => client.Id == id);

    private bool Minimized(ClientId id)
    {
        var index = FindClient(id);
        return index >= 0 && _clients[index].Minimized;
    }

    private void MarkMinimized(ClientId id, bool minimized)
    {
        var index = FindClient(id);
        if (index >= 0)
            _clients[index] = _clients[index] with { Minimized = minimized };
    }

    private void MarkFloating(ClientId id, bool floating)
    {
        var index = FindClient(id);
        if (index >= 0)
            _clients[index] = _clients[index] with { Floating = floating };
    }

    private static WindowId WindowOf(Tile tile) => new(tile.Id.Value);

    private static ClientId Id(WindowId window) => new(window.Value);

    private static bool Contains(Rectangle box, Point point) =>
        box.Width > 0
        && box.Height > 0
        && point.X >= box.X
        && point.Y >= box.Y
        && point.X < box.X + box.Width
        && point.Y < box.Y + box.Height;

    private static double DistanceSquared(Rectangle box, Point point)
    {
        var dx = point.X < box.X ? box.X - point.X : point.X >= box.X + box.Width ? point.X - (box.X + box.Width) : 0;
        var dy = point.Y < box.Y ? box.Y - point.Y : point.Y >= box.Y + box.Height ? point.Y - (box.Y + box.Height) : 0;
        return (dx * (double)dx) + (dy * (double)dy);
    }

    private static void Rotate(List<Tile> list, int first, int middle, int last)
    {
        if (middle <= first || middle >= last || last > list.Count)
            return;

        var count = last - first;
        var offset = middle - first;
        var segment = new Tile[count];
        for (var i = 0; i < count; i++)
            segment[i] = list[first + i];

        for (var i = 0; i < count; i++)
            list[first + i] = segment[(i + offset) % count];
    }

    private static void SwapIds(Tile left, Tile right) =>
        (left.Id, right.Id) = (right.Id, left.Id);

    private sealed class Tile
    {
        public ClientId Id { get; set; }

        public bool Master { get; set; }

        public double MasterFraction { get; set; } = 0.55;

        public double SizeFraction { get; set; } = 1;

        public Rectangle Box { get; set; }
    }

    private sealed record Suspended(Tile Tile, int Index, Tile? Promoted);
}
