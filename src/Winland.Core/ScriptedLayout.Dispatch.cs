using static Winland.OrdinalText;

namespace Winland;

// What the dispatchers do in a script's Layout, as Winland's Lua layouts do: focus moves to the tile beside
// by geometry, movewindow and swapwindow exchange places in the order the script reads, and layoutmsg goes to the
// script's layout_msg. A floating Client moves, resizes and is dragged as in every Layout.
internal sealed partial class ScriptedLayout : IMouseDragHost, IDragOwner
{
    private readonly MouseDrag _drag = new();

    MouseDrag IDragOwner.Drag => _drag;
    private Func<WindowId, int>? _recency;

    public void RankFocus(Func<WindowId, int>? recency) => _recency = recency;

    public void NoteFocus(WindowId window)
    {
    }

    public IReadOnlyList<Placement> Preselect(string direction)
    {
        ArgumentNullException.ThrowIfNull(direction);
        return [];
    }

    public IReadOnlyList<Placement> TogglePseudo(WindowId window)
    {
        var id = new ClientId(window.Value);
        if (IndexOf(id) < 0)
            return [];

        _pseudo.Toggle(id);
        return Placed();
    }

    public WindowId? MoveFocus(WindowId active, string arguments)
    {
        if (!TrySide(First(arguments), out var side) || !TryGeometry(active, out var origin, out var floating))
            return null;

        if (floating)
            return DirectionalNeighbor.FindByAngle(_clients, origin, side, active, (WindowId window, out Rectangle box) => TryGeometry(window, out box, out _));

        return Neighbor(origin, side, active, false) ?? Neighbor(origin, side, active, true);
    }

    private WindowId? Neighbor(Rectangle origin, Side side, WindowId except, bool floating) =>
        DirectionalNeighbor.Find(_clients, origin, side, except, floating, (WindowId window, out Rectangle box) => TryGeometry(window, out box, out _), _recency);

    // The tile after the one that closed is the last one in the order, as Winland picks it.
    public WindowId? FocusAfterClose() => _order.Count == 0 ? null : new WindowId(_order[^1].Value);

    /// <summary>
    /// A tile moves one place back in the order for left and up and one place on for right and down, and stays at either
    /// end, as Winland moves a target of a Lua layout. Its box goes with it until the script answers.
    /// </summary>
    public IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments)
    {
        var id = new ClientId(window.Value);
        if (Minimized(id) || !TrySide(First(arguments), out var side))
            return [];

        if (_floating.Contains(id))
            return _floating.MoveToEdge(id, side, _options.FloatingWorkArea(_area), _options.BorderFor(id));

        var index = _order.IndexOf(id);
        if (index < 0)
            return [];

        var other = side is Side.Left or Side.Up ? index - 1 : index + 1;
        if (other < 0 || other >= _order.Count)
            return [];

        Exchange(id, _order[other]);
        return Placed();
    }

    public IReadOnlyList<Placement> Swap(WindowId active, WindowId other)
    {
        var a = new ClientId(active.Value);
        var b = new ClientId(other.Value);
        if (a == b || !_order.Contains(a) || !_order.Contains(b))
            return [];

        Exchange(a, b);
        return Placed();
    }

    private void Exchange(ClientId a, ClientId b)
    {
        var boxes = Boxes();
        var first = _order.IndexOf(a);
        var second = _order.IndexOf(b);
        (_order[first], _order[second]) = (_order[second], _order[first]);
        _boxes.Clear();
        foreach (var (id, box) in boxes)
            _boxes[id] = box;

        (_boxes[a], _boxes[b]) = (_boxes[b], _boxes[a]);
        _boxesArea = TilingArea();
        Request();
    }

    // A tile keeps the box the script gave it, so only a floating Client resizes.
    public IReadOnlyList<Placement> ResizeActive(WindowId window, string arguments)
    {
        var id = new ClientId(window.Value);
        if (Minimized(id) || !_floating.Contains(id))
            return [];

        if (!ResizeArguments.TryParse(arguments, out var x, out var y, out var exact))
            return [];

        return _floating.Resize(id, x, y, exact, _options.FloatingOnScreen(_area));
    }

    /// <summary>
    /// Hands the message to the script's <c>layout_msg</c>, which lays the tiles out again. It is answered later, so an
    /// error the script gives is logged rather than replied, and the message is always accepted here.
    /// </summary>
    public IReadOnlyList<Placement> LayoutMessage(WindowId? active, string arguments)
    {
        _ = active;
        ArgumentNullException.ThrowIfNull(arguments);
        LayoutMessageError = null;
        Request(arguments);
        return [];
    }

    public IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag) => _drag.Step(this, window, drag);

    FloatingClients IMouseDragHost.Floating => _floating;

    int IMouseDragHost.ResizeCorner => _options.ResizeCorner;

    bool IMouseDragHost.TryDragBounds(ClientId id, out Rectangle bounds, out bool floating)
    {
        floating = _floating.TryGet(id, out bounds);
        if (floating)
            return !Minimized(id);

        return TryBounds(new WindowId(id.Value), out bounds);
    }

    Rectangle IMouseDragHost.SnapFloating(ClientId self, Rectangle proposed, SnapEdges edges, bool resize) =>
        FloatingSnap.AmongClients(this, self, proposed, edges, resize, _area, _monitor, _options);

    bool IMouseDragHost.LiftTile(ClientId id, out Rectangle box)
    {
        box = default;
        if (!_order.Contains(id))
            return false;

        box = Present(id, Boxes()[id]);
        DropTiled(id);
        SetFloating(id, true);
        _floating.Set(id, box);
        Request();
        return true;
    }

    // A tile keeps the box the script gave it, as Winland only lays a Lua layout out again.
    IReadOnlyList<Placement> IMouseDragHost.ResizeTile(ClientId id, int dx, int dy, (bool Left, bool Top) corner) => [];

    // A tile dropped back joins the end of the order, as a target moved into a Lua layout does.
    IReadOnlyList<Placement> IMouseDragHost.DropTile(ClientId id, Point pointer)
    {
        _ = pointer;
        _floating.Remove(id);
        SetFloating(id, false);
        _order.Add(id);
        Request();
        return Visible();
    }

    IReadOnlyList<Placement> IMouseDragHost.ShowFloating(ClientId id) => Visible();

    IReadOnlyList<Placement> IMouseDragHost.EndInPlace() => [];

    // A floating Client's rectangle, or a tile's box before gaps, so tiles that meet are neighbours.
    private bool TryGeometry(WindowId window, out Rectangle box, out bool floating)
    {
        var id = new ClientId(window.Value);
        floating = _floating.TryGet(id, out box);
        if (floating)
            return !Minimized(id);

        if (!_order.Contains(id))
            return false;

        box = Boxes()[id];
        return true;
    }

    // A direction by its word or its first letter, as Winland reads one.
    private static bool TrySide(string text, out Side side)
    {
        side = default;
        if (text.Length == 0)
            return false;

        switch (char.ToLowerInvariant(text[0]))
        {
            case 'l':
                side = Side.Left;
                return text.Length == 1 || Is(text, "left");
            case 'r':
                side = Side.Right;
                return text.Length == 1 || Is(text, "right");
            case 'u':
            case 't':
                side = Side.Up;
                return text.Length == 1 || Is(text, "up") || Is(text, "top");
            case 'd':
            case 'b':
                side = Side.Down;
                return text.Length == 1 || Is(text, "down") || Is(text, "bottom");
            default:
                return false;
        }
    }

    private static string First(string arguments)
    {
        var tokens = arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? "" : tokens[0];
    }
}
