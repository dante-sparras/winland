namespace Winland;

/// <summary>
/// A Layout a config's script arranges, such as one <c>hl.layout.register</c> makes, named <c>lua:NAME</c>. It keeps the
/// tiled Clients in order, appending each new one as the pinned release does, and asks <see cref="ILayoutScripts"/> for
/// their boxes whenever the order or the tiling area changes. The answer comes later, through <c>layoutplace</c>, since
/// the script never runs inside the core's lock (ADR 0013). Until it comes the Clients keep the boxes they had, and when
/// one has none yet, or the script failed, every tile takes a cell of an even grid, as the pinned release falls back.
/// </summary>
internal sealed partial class ScriptedLayout : Layout
{
    private static long _lastId;

    private readonly ILayoutScripts? _scripts;
    private readonly List<Client> _clients = [];
    private readonly List<ClientId> _order = [];
    private readonly FloatingClients _floating = new();
    private readonly Dictionary<ClientId, Rectangle> _natural = [];
    private readonly PseudoTiles _pseudo = new();
    private readonly Dictionary<ClientId, SizeLimits> _limits = [];

    // The boxes the script placed, before gaps and border, in the tiling area they were placed in.
    private readonly Dictionary<ClientId, Rectangle> _boxes = [];
    private Rectangle _boxesArea;

    private LayoutOptions _options = LayoutOptions.Unconfigured;
    private Rectangle _area;
    private Rectangle _monitor;

    // The last request: its generation, and the area and order it was for.
    private long _generation;
    private Rectangle _requestedArea;
    private ClientId[]? _requestedOrder;

    public ScriptedLayout(string name, ILayoutScripts? scripts)
    {
        Name = name;
        _scripts = scripts;
        Id = Interlocked.Increment(ref _lastId);
    }

    /// <summary>The registered name, <c>lua:NAME</c>, which <c>general:layout</c> and a workspace rule select.</summary>
    public string Name { get; }

    /// <summary>The number that names this Layout in its requests and in <c>layoutplace</c>.</summary>
    public long Id { get; }

    public IReadOnlyList<Client> Clients => _clients;

    public string? LayoutMessageError { get; private set; }

    public void Readdress(WorkspaceId workspace)
    {
        for (var i = 0; i < _clients.Count; i++)
            _clients[i] = _clients[i] with { Workspace = workspace };
    }

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

    public void NudgeFloating(int dx, int dy) => _floating.Nudge(dx, dy);

    public IReadOnlyList<Placement> Apply(LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        Request();
        return Placed();
    }

    public void Fit(Rectangle area)
    {
        _area = area;
        Request();
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
        _area = area;
        if (monitor.Width > 0 && monitor.Height > 0)
            _monitor = monitor;

        var id = new ClientId(window.Value);
        if (IndexOf(id) >= 0)
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

        _order.Add(id);
        Request();
        return Placed();
    }

    public IReadOnlyList<Placement> Remove(WindowId window)
    {
        var id = new ClientId(window.Value);
        var index = IndexOf(id);
        if (index < 0)
            return [];

        _ = _drag.Forget(id);
        var tiled = DropTiled(id);
        _floating.Forget(id);
        _natural.Remove(id);
        _pseudo.Forget(id);
        _limits.Remove(id);
        _clients.RemoveAt(index);
        if (!tiled)
            return [];

        Request();
        return Placed();
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
        Request();
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

        _order.Add(id);
        Request();
        return Placed();
    }

    public bool TryTake(WindowId window, out Taken taken)
    {
        taken = default;
        var id = new ClientId(window.Value);
        var index = IndexOf(id);
        if (index < 0)
            return false;

        var bounds = _floating.TryGet(id, out var floating)
            ? floating
            : _natural.TryGetValue(id, out var natural) ? natural : default;
        _limits.TryGetValue(id, out var limits);
        taken = SlotHandover.Describe(_clients[index], bounds, limits);
        Remove(window);
        return true;
    }

    public bool Substitute(WindowId current, WindowId next, Taken incoming, out Taken outgoing)
    {
        outgoing = default;
        var from = new ClientId(current.Value);
        var to = new ClientId(next.Value);
        var index = IndexOf(from);
        if (index < 0 || from == to || IndexOf(to) >= 0)
            return false;

        var client = _clients[index];
        var bounds = _floating.TryGet(from, out var floating)
            ? floating
            : _natural.TryGetValue(from, out var natural) ? natural : default;
        _limits.TryGetValue(from, out var limits);
        outgoing = SlotHandover.Describe(client, bounds, limits);

        _clients[index] = SlotHandover.Rename(client, next, incoming);
        SlotHandover.Move(_order, from, to);
        SlotHandover.Move(_boxes, from, to);
        _floating.Hand(from, to);
        _pseudo.Hand(from, to);
        _natural.Remove(from);
        _natural[to] = incoming.Bounds;
        _limits.Remove(from);
        _limits[to] = new SizeLimits(incoming.MinSize, incoming.MaxSize) { Tiled = incoming.TiledLimits };
        Request();
        return true;
    }

    // Every tile is where the script put it, so a tile's limits only grow it over its neighbours. A floating Client is
    // clamped to them.
    public IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize, TiledLimits? tiledLimits = null)
    {
        var id = new ClientId(window.Value);
        if (IndexOf(id) < 0)
            return [];

        _limits[id] = new SizeLimits(minSize, maxSize) { Tiled = tiledLimits };
        if (_floating.Contains(id))
            return _floating.Clamp(id, new SizeLimits(minSize, maxSize));

        return Placed();
    }

    public bool TryBounds(WindowId window, out Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        if (_floating.TryGet(id, out bounds))
            return true;

        if (_order.Contains(id))
        {
            bounds = Present(id, Boxes()[id]);
            return true;
        }

        bounds = default;
        return false;
    }

    public IReadOnlyList<Placement> Visible()
    {
        var placements = new List<Placement>(Placed());
        _floating.AddShown(placements, _clients);
        return placements;
    }

    public IReadOnlyList<Placement> Placed()
    {
        var boxes = Boxes();
        var placements = new List<Placement>(_order.Count);
        foreach (var id in _order)
            placements.Add(new Placement(id, Present(id, boxes[id])));

        return placements;
    }

    public IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode) =>
        FullscreenClients.Assign(_clients, window, mode, clientMode) ? Visible() : [];

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        return _floating.Place(id, bounds, _options.FloatingOnScreen(_area)) ? Visible() : [];
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
            _order.Add(id);
            Request();
            return Placed();
        }

        if (!_order.Contains(id))
            return [];

        var tile = Present(id, Boxes()[id]);
        DropTiled(id);
        var floated = _floating.Around(id, tile, _options.FloatingOnScreen(_area));
        _floating.Set(id, floated);
        SetFloating(id, true);
        Request();
        return [.. Placed(), new Placement(id, floated)];
    }

    /// <summary>
    /// Takes the script's answer to the request of <paramref name="generation"/>: the boxes it placed, by window, or null
    /// when it failed. An answer to an older request is dropped, since a newer one is on its way. False when nothing
    /// changed.
    /// </summary>
    internal bool Accept(long generation, IReadOnlyList<(WindowId Window, LayoutBox Box)>? placed)
    {
        if (generation != _generation || _requestedOrder is null)
            return false;

        if (placed is null)
        {
            _boxes.Clear();
            return true;
        }

        // A target the script did not place keeps its box, the grid's when it had none.
        var current = Boxes();
        _boxes.Clear();
        foreach (var (id, box) in current)
            _boxes[id] = box;

        _boxesArea = _requestedArea;
        foreach (var (window, box) in placed)
        {
            var id = new ClientId(window.Value);
            if (_order.Contains(id))
                _boxes[id] = Edges(box);
        }

        return true;
    }

    // Asks the script to lay the tiles out again when their order or the tiling area changed, or to take a message.
    private void Request(string? message = null)
    {
        if (_scripts is null || _area.Width <= 0 || _area.Height <= 0)
            return;

        var area = TilingArea();
        var order = _order.ToArray();
        if (message is null && _requestedOrder is not null && area == _requestedArea && order.SequenceEqual(_requestedOrder))
            return;

        var boxes = Boxes();
        _generation++;
        _requestedArea = area;
        _requestedOrder = order;
        var targets = Array.ConvertAll(order, id => new LayoutTarget(new WindowId(id.Value), boxes[id]));
        _scripts.Arrange(new LayoutRequest(Id, _generation, Name, area, targets) { Message = message });
    }

    // The box of each tile before gaps and border: the script's, carried to the current tiling area, or the grid's
    // when a tile has none.
    private Dictionary<ClientId, Rectangle> Boxes()
    {
        var area = TilingArea();
        var boxes = new Dictionary<ClientId, Rectangle>(_order.Count);
        if (_order.TrueForAll(_boxes.ContainsKey))
        {
            foreach (var id in _order)
                boxes[id] = Carry(_boxes[id], _boxesArea, area);

            return boxes;
        }

        var count = _order.Count;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        var rows = Math.Max(1, (int)Math.Ceiling(count / (double)columns));
        for (var i = 0; i < count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            boxes[_order[i]] = Edges(new LayoutBox(
                area.X + (area.Width * column / (double)columns),
                area.Y + (area.Height * row / (double)rows),
                area.Width / (double)columns,
                area.Height / (double)rows));
        }

        return boxes;
    }

    // A box placed in one tiling area, moved and scaled into another, as when gaps change or the Workspace moves.
    private static Rectangle Carry(Rectangle box, Rectangle from, Rectangle to)
    {
        if (from == to || from.Width <= 0 || from.Height <= 0)
            return box;

        var scaleX = to.Width / (double)from.Width;
        var scaleY = to.Height / (double)from.Height;
        return Edges(new LayoutBox(
            to.X + ((box.X - from.X) * scaleX),
            to.Y + ((box.Y - from.Y) * scaleY),
            box.Width * scaleX,
            box.Height * scaleY));
    }

    // A fractional box in whole pixels, each edge rounded, so boxes that meet still meet. A negative size is none.
    private static Rectangle Edges(LayoutBox box)
    {
        var left = Round(box.X);
        var top = Round(box.Y);
        var right = Round(box.X + Math.Max(0, box.Width));
        var bottom = Round(box.Y + Math.Max(0, box.Height));
        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static int Round(double value) =>
        double.IsFinite(value) ? (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue / 2, int.MaxValue / 2) : 0;

    // A tile's frame: gaps_in on the sides that do not meet the tiling area's edge, then the border, as the pinned
    // release presents a box a script places.
    private Rectangle Present(ClientId id, Rectangle box)
    {
        var area = TilingArea();
        var gaps = _options.GapsIn;
        var left = box.X == area.X ? 0 : gaps.Left;
        var top = box.Y == area.Y ? 0 : gaps.Top;
        var right = box.X + box.Width == area.X + area.Width ? 0 : gaps.Right;
        var bottom = box.Y + box.Height == area.Y + area.Height ? 0 : gaps.Bottom;
        var presented = _options.KeepAspect(Shrink(box, left, top, right, bottom), _area, _order.Count);
        _natural.TryGetValue(id, out var natural);
        presented = _pseudo.Frame(id, presented, natural, _options.BorderFor(id));
        return _limits.TryGetValue(id, out var limits) ? limits.ForTiles.Raise(presented, area) : presented;
    }

    private Rectangle TilingArea() => _options.GapsOut.Apply(_area);

    private static Rectangle Shrink(Rectangle box, int left, int top, int right, int bottom)
    {
        var width = Math.Max(0, box.Width - left - right);
        var height = Math.Max(0, box.Height - top - bottom);
        return new Rectangle(box.X + left, box.Y + top, width, height);
    }

    private bool DropTiled(ClientId id)
    {
        _boxes.Remove(id);
        return _order.Remove(id);
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
}
