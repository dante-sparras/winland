namespace Winland;

internal sealed partial class MasterLayout : IMouseDragHost
{
    private static readonly (string Word, Side Side)[] Directions =
    [
        ("l", Side.Left),
        ("left", Side.Left),
        ("r", Side.Right),
        ("right", Side.Right),
        ("u", Side.Up),
        ("up", Side.Up),
        ("t", Side.Up),
        ("top", Side.Up),
        ("d", Side.Down),
        ("down", Side.Down),
        ("b", Side.Down),
        ("bottom", Side.Down),
    ];

    private readonly MouseDrag _drag = new();

    public WindowId? MoveFocus(WindowId active, string arguments)
    {
        if (!TryDirection(First(arguments), out var direction))
            return null;
        if (!TryBox(active, out var origin, out var floating))
            return null;

        if (floating)
            return DirectionalNeighbor.FindByAngle(_clients, origin, direction, active, (WindowId window, out Rectangle box) => TryBox(window, out box, out _));

        return Neighbor(origin, direction, active, false)
            ?? Neighbor(origin, direction, active, true);
    }

    private Func<WindowId, int>? _recency;

    public void RankFocus(Func<WindowId, int>? recency) => _recency = recency;

    private WindowId? Neighbor(Rectangle origin, Side side, WindowId except, bool floating) =>
        DirectionalNeighbor.Find(_clients, origin, side, except, floating, (WindowId window, out Rectangle box) => TryBox(window, out box, out _), _recency);

    public void NoteFocus(WindowId window)
    {
        if (FindClient(Id(window)) >= 0)
            _focused = window;
    }

    public IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments)
    {
        if (!TryDirection(First(arguments), out var direction))
            return [];
        if (Minimized(Id(window)))
            return [];

        if (_floating.Contains(Id(window)))
            return _floating.MoveToEdge(Id(window), direction, _layout.FloatingWorkArea(_area));

        var tile = Find(window);
        if (tile is null)
            return [];

        var neighbor = Neighbor(tile.Box, direction, window, floating: false);
        if (neighbor is not WindowId other || Find(other) is not Tile destination)
            return [];

        SwapIds(tile, destination);
        Recalculate();
        return Placed();
    }

    public IReadOnlyList<Placement> ResizeActive(WindowId window, string arguments)
    {
        if (!ResizeArguments.TryParse(arguments, out var x, out var y, out var exact))
            return [];
        if (Minimized(Id(window)))
            return [];

        if (_floating.Contains(Id(window)))
            return _floating.Resize(Id(window), x, y, exact);

        var tile = Find(window);
        if (tile is null)
            return [];

        // exact names the window's size, which is the tile less its gaps and border.
        var shown = Present(tile);
        var dx = exact ? x - shown.Width : x;
        var dy = exact ? y - shown.Height : y;
        ResizeTile(tile, dx, dy);
        return Placed();
    }

    public IReadOnlyList<Placement> ToggleFloating(WindowId window)
    {
        var id = Id(window);
        var index = FindClient(id);
        if (index < 0 || _clients[index].Minimized)
            return [];

        if (_floating.TryGet(id, out var floatingBox) && _floating.Sink(id))
        {
            MarkFloating(id, false);
            var tile = new Tile { Id = id, MasterFraction = _master.Mfact };
            if (!Adopt(tile, firstMap: true, dragging: false, pointer: null))
                Refloat(id, floatingBox);
            _focused = window;
            return Visible();
        }

        var existing = Find(window);
        if (existing is null)
            return [];

        var tileBox = existing.Box;
        Detach(existing);
        var floated = _floating.Around(id, tileBox, _layout.FloatingWorkArea(_area));
        _floating.Set(id, floated);
        MarkFloating(id, true);
        var placements = new List<Placement>(Placed()) { new(id, floated) };
        return placements;
    }

    public IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag) => _drag.Step(this, window, drag);

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds)
    {
        var id = Id(window);
        return _floating.Place(id, bounds) ? Visible() : [];
    }

    FloatingClients IMouseDragHost.Floating => _floating;

    int IMouseDragHost.ResizeCorner => _layout.ResizeCorner;

    bool IMouseDragHost.TryDragBounds(ClientId id, out Rectangle bounds, out bool floating) =>
        TryBox(new WindowId(id.Value), out bounds, out floating) && !Minimized(id);

    Rectangle IMouseDragHost.SnapFloating(ClientId self, Rectangle proposed, SnapEdges edges, bool resize) =>
        FloatingSnap.AmongClients(this, self, proposed, edges, resize, _area, _monitor, _layout);

    bool IMouseDragHost.LiftTile(ClientId id, out Rectangle box)
    {
        box = default;
        var tile = Find(new WindowId(id.Value));
        if (tile is null)
            return false;

        box = Present(tile);
        Detach(tile);
        MarkFloating(id, true);
        _floating.Set(id, box);
        return true;
    }

    IReadOnlyList<Placement> IMouseDragHost.ResizeTile(ClientId id, int dx, int dy, (bool Left, bool Top) corner)
    {
        var tile = Find(new WindowId(id.Value));
        if (tile is null)
            return [];

        ResizeTile(tile, dx, dy, corner);
        return Placed();
    }

    IReadOnlyList<Placement> IMouseDragHost.DropTile(ClientId id, Point pointer)
    {
        var lifted = _floating.TryGet(id, out var liftedBox);
        _floating.Remove(id);
        MarkFloating(id, false);
        var tile = new Tile { Id = id, MasterFraction = _master.Mfact };
        if (!Adopt(tile, firstMap: false, dragging: true, pointer: pointer))
            Refloat(id, lifted ? liftedBox : _natural.GetValueOrDefault(id));
        _focused = new WindowId(id.Value);
        return Visible();
    }

    // A Client that cannot fill its tile stays floating where it was.
    private void Refloat(ClientId id, Rectangle box)
    {
        _floating.Set(id, box);
        MarkFloating(id, true);
    }

    IReadOnlyList<Placement> IMouseDragHost.ShowFloating(ClientId id) => Visible();

    IReadOnlyList<Placement> IMouseDragHost.EndInPlace() => [];

    private bool TryBox(WindowId window, out Rectangle box, out bool floating)
    {
        var id = Id(window);
        if (_floating.TryGet(id, out box))
        {
            floating = true;
            return true;
        }

        floating = false;
        var tile = Find(window);
        if (tile is null)
        {
            box = default;
            return false;
        }

        box = tile.Box;
        return true;
    }

    private static bool Sticks(int edge, int other) => Math.Abs(edge - other) <= 1;

    private static bool TryDirection(string text, out Side direction)
    {
        foreach (var (word, parsed) in Directions)
        {
            if (text.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                direction = parsed;
                return true;
            }
        }

        direction = Side.Left;
        return false;
    }

    private static string First(string arguments)
    {
        var tokens = Tokens(arguments);
        return tokens.Length == 0 ? "" : tokens[0];
    }
}
