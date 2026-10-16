namespace Winland;

/// <summary>
/// The floating Clients of one Layout: the rectangle each one floats at, and the size each Client
/// last had while floating, which it gets back the next time it floats. Every Layout floats the same way.
/// </summary>
internal sealed class FloatingClients
{
    private const int DefaultWidth = 640;
    private const int DefaultHeight = 400;

    private readonly Dictionary<ClientId, Rectangle> _boxes = [];
    private readonly Dictionary<ClientId, (int Width, int Height)> _lastSize = [];

    public bool Contains(ClientId id) => _boxes.ContainsKey(id);

    public bool TryGet(ClientId id, out Rectangle box) => _boxes.TryGetValue(id, out box);

    public void Set(ClientId id, Rectangle box) => _boxes[id] = box;

    /// <summary>Stops floating a Client without remembering its size, as a dragged tile does when it drops.</summary>
    public bool Remove(ClientId id) => _boxes.Remove(id);

    /// <summary>Remembers the size a Window asked for, which it floats at until it has floated.</summary>
    public void Remember(ClientId id, Rectangle bounds)
    {
        if (bounds.Width > 0 && bounds.Height > 0)
            _lastSize[id] = (bounds.Width, bounds.Height);
    }

    /// <summary>Forgets a Client that leaves the Layout. True when it was floating.</summary>
    public bool Forget(ClientId id)
    {
        _lastSize.Remove(id);
        return _boxes.Remove(id);
    }

    /// <summary>Stops floating a Client that goes back to its tile, and remembers its size. False when it was not floating.</summary>
    public bool Sink(ClientId id)
    {
        if (!_boxes.Remove(id, out var box))
            return false;

        Remember(id, box);
        return true;
    }

    /// <summary>
    /// Where a tiled Client floats: around the middle of its tile, at the size it last floated at
    /// or 640 by 400, inside the floating work area. A size that would barely differ from the tile grows by 10.
    /// </summary>
    public Rectangle Around(ClientId id, Rectangle tile, Rectangle work)
    {
        var width = DefaultWidth;
        var height = DefaultHeight;
        if (_lastSize.TryGetValue(id, out var last) && last.Width >= 5 && last.Height >= 5)
        {
            width = last.Width;
            height = last.Height;
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
        return FitInWorkArea(floated, work);
    }

    /// <summary>Hands a floating slot and its remembered size to the Window that takes over the slot.</summary>
    public void Hand(ClientId from, ClientId to)
    {
        SlotHandover.Move(_boxes, from, to);
        SlotHandover.Move(_lastSize, from, to);
    }

    /// <summary>Shifts every floating Client by a Monitor move.</summary>
    public void Nudge(int dx, int dy)
    {
        if ((dx == 0 && dy == 0) || _boxes.Count == 0)
            return;

        foreach (var id in _boxes.Keys.ToArray())
        {
            var box = _boxes[id];
            _boxes[id] = box with { X = box.X + dx, Y = box.Y + dy };
        }
    }

    /// <summary>Keeps a floating Client inside its size limits. Empty when it already was.</summary>
    public IReadOnlyList<Placement> Clamp(ClientId id, SizeLimits limits)
    {
        if (!_boxes.TryGetValue(id, out var box))
            return [];

        var clamped = limits.Clamp(box);
        if (clamped == box)
            return [];

        _boxes[id] = clamped;
        return [new Placement(id, clamped)];
    }

    /// <summary>Moves a floating Client against one edge of the floating work area. Empty when it already is there.</summary>
    public IReadOnlyList<Placement> MoveToEdge(ClientId id, Side side, Rectangle frame)
    {
        if (!_boxes.TryGetValue(id, out var box))
            return [];

        var moved = side switch
        {
            Side.Left => box with { X = frame.X },
            Side.Right => box with { X = frame.X + frame.Width - box.Width },
            Side.Up => box with { Y = frame.Y },
            Side.Down => box with { Y = frame.Y + frame.Height - box.Height },
            _ => box,
        };
        if (moved == box)
            return [];

        _boxes[id] = moved;
        return [new Placement(id, moved)];
    }

    /// <summary>Resizes a floating Client about its center, by a delta or to an exact size.</summary>
    public IReadOnlyList<Placement> Resize(ClientId id, int x, int y, bool exact)
    {
        if (!_boxes.TryGetValue(id, out var box))
            return [];

        var deltaX = exact ? x - box.Width : x;
        var deltaY = exact ? y - box.Height : y;
        var resized = new Rectangle(box.X - (deltaX / 2), box.Y - (deltaY / 2), box.Width + deltaX, box.Height + deltaY);
        if (resized.Width < 1 || resized.Height < 1)
            return [];

        _boxes[id] = resized;
        return [new Placement(id, resized)];
    }

    /// <summary>Moves a floating Client to a rectangle of at least one pixel. False when it is not floating.</summary>
    public bool Place(ClientId id, Rectangle bounds)
    {
        if (!_boxes.ContainsKey(id) || bounds.Width < 1 || bounds.Height < 1)
            return false;

        _boxes[id] = bounds;
        return true;
    }

    /// <summary>Adds the floating Clients that are shown, in the Layout's Client order.</summary>
    public void AddShown(List<Placement> placements, IReadOnlyList<Client> clients)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(clients);
        foreach (var client in clients)
        {
            if (!client.Floating || client.Minimized)
                continue;

            if (_boxes.TryGetValue(client.Id, out var box))
                placements.Add(new Placement(client.Id, box));
        }
    }

    /// <summary>A rectangle moved, not resized, to lie inside the work area. An empty work area leaves it.</summary>
    public static Rectangle FitInWorkArea(Rectangle box, Rectangle work)
    {
        if (work.Width <= 0 || work.Height <= 0)
            return box;

        var x = Math.Max(box.X, work.X);
        var y = Math.Max(box.Y, work.Y);
        if (x + box.Width > work.X + work.Width)
            x = work.X + work.Width - box.Width;
        if (y + box.Height > work.Y + work.Height)
            y = work.Y + work.Height - box.Height;

        return new Rectangle(x, y, box.Width, box.Height);
    }
}
