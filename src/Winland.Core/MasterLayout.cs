namespace Winland;

internal sealed partial class MasterLayout : Layout
{
    private readonly List<Client> _clients = [];
    private readonly List<Tile> _tiles = [];
    private readonly Dictionary<ClientId, Rectangle> _floating = [];
    private readonly Dictionary<ClientId, Rectangle> _natural = [];
    private readonly Dictionary<ClientId, ClientLimits> _limits = [];
    private readonly Dictionary<ClientId, Suspended> _suspended = [];
    private LayoutOptions _layout = LayoutOptions.Unconfigured;
    private Rectangle _monitor;
    private MasterOptions _master = MasterOptions.Documented;
    private MasterOrientation? _explicit;
    private WindowId? _focused;
    private Rectangle _area;

    public IReadOnlyList<Client> Clients => _clients;

    public bool FollowsMonitorWhenUnmoved => true;

    public void NudgeFloating(int dx, int dy) => FloatingShift.By(_floating, dx, dy);

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
        Size? maxSize = null)
    {
        _ = pointer;
        _ = activeWorkspace;
        _monitor = monitor;
        _area = area;
        var id = Id(window);
        if (Known(id))
            return [];

        _limits[id] = new ClientLimits(minSize, maxSize);
        _natural[id] = bounds;
        var client = Adopted(id, window, workspace, windowClass, title, floating, processId, initialClass, initialTitle);
        if (floating)
        {
            var placed = ClampToLimits(bounds, minSize, maxSize);
            _clients.Add(client);
            _floating[id] = placed;
            _focused = window;
            return [new Placement(id, placed)];
        }

        _clients.Add(client);
        var tile = new Tile { Id = id, MasterFraction = _master.Mfact };
        Adopt(tile, firstMap: true, dragging: false, pointer: null);
        _focused = window;
        return Placed();
    }

    public IReadOnlyList<Placement> Remove(WindowId window)
    {
        var id = Id(window);
        _natural.Remove(id);
        _limits.Remove(id);
        if (_floating.Remove(id) || _suspended.Remove(id))
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
        if (_floating.ContainsKey(id))
        {
            MarkMinimized(id, true);
            return [];
        }

        var tile = Find(window);
        if (tile is null)
            return [];

        var index = _tiles.IndexOf(tile);
        Detach(tile);
        _suspended[id] = new Suspended(tile, index);
        MarkMinimized(id, true);
        return Placed();
    }

    public IReadOnlyList<Placement> Restore(WindowId window)
    {
        var id = Id(window);
        if (_floating.TryGetValue(id, out var bounds))
        {
            if (!Minimized(id))
                return [];

            MarkMinimized(id, false);
            return [new Placement(id, bounds)];
        }

        if (!_suspended.Remove(id, out var saved))
            return [];

        MarkMinimized(id, false);
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

    // A maximum is remembered and handed on with the Client. It clamps a floating window.
    // It does not shrink a tiled master or slave: the master fraction owns that size.
    public IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize)
    {
        var id = Id(window);
        if (FindClient(id) < 0)
            return [];

        _limits[id] = new ClientLimits(minSize, maxSize);
        if (_floating.TryGetValue(id, out var floating))
        {
            var clamped = ClampToLimits(floating, minSize, maxSize);
            if (clamped == floating)
                return [];

            _floating[id] = clamped;
            return [new Placement(id, clamped)];
        }

        if (Find(window) is null && !_suspended.ContainsKey(id))
            return [];

        Recalculate();
        return Placed();
    }

    public bool TryBounds(WindowId window, out Rectangle bounds)
    {
        var id = Id(window);
        if (_floating.TryGetValue(id, out bounds))
            return true;

        var tile = Find(window);
        if (tile is not null)
        {
            bounds = Present(tile.Box);
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
        foreach (var client in _clients)
        {
            if (!client.Floating || client.Minimized)
                continue;

            if (_floating.TryGetValue(client.Id, out var bounds))
                placements.Add(new Placement(client.Id, bounds));
        }

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
                placements.Add(new Placement(client.Id, Present(tile.Box)));
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
        _ = window;
        return [];
    }

    public WindowId? FocusAfterClose()
    {
        if (!_master.FocusMasterOnClose)
            return null;

        var master = FirstMaster();
        return master is null ? null : WindowOf(master);
    }

    private void Adopt(Tile tile, bool firstMap, bool dragging, Point? pointer)
    {
        var centeredBefore = Dynamic() == MasterOrientation.Center && SlaveCount() >= _master.SlaveCountForCenter;
        var centerCursor = dragging && _master.DropAtCursor && centeredBefore;
        var dragOntoMaster = false;
        if (dragging && pointer is Point point && (_tiles.Count > 1 || !_master.DropAtCursor) && !centerCursor
            && Closest(point) is { Master: true })
            dragOntoMaster = true;

        PlaceNew(tile, dragOntoMaster || _master.NewStatus == MasterNewStatus.Master);

        var forceMaster = dragOntoMaster;
        if (dragging && pointer is Point cursor && _master.DropAtCursor)
            forceMaster |= DropAtCursor(tile, cursor, centeredBefore);

        AssignRole(tile, firstMap, dragging, forceMaster);
        Recalculate();
    }

    private void PlaceNew(Tile tile, bool becomesMaster)
    {
        if (_master.NewOnActive != MasterNewOnActive.None && !becomesMaster)
        {
            var focus = Find(_focused);
            if (focus is not null && !(focus.Master && (MasterCount() == 1 || _master.NewStatus == MasterNewStatus.Slave)))
            {
                var index = _tiles.IndexOf(focus);
                if (_master.NewOnActive != MasterNewOnActive.Before)
                    index++;

                _tiles.Insert(Math.Clamp(index, 0, _tiles.Count), tile);
                return;
            }
        }

        if (_master.NewOnTop)
            _tiles.Insert(0, tile);
        else
            _tiles.Add(tile);
    }

    private bool DropAtCursor(Tile tile, Point pointer, bool centeredBefore)
    {
        if (centeredBefore && TryCenterDrop(tile, pointer, out var force))
            return force;

        if (_tiles.Count > 2)
        {
            ReorderToCursor(tile, pointer);
            return false;
        }

        return _tiles.Count == 2 && CursorOnMasterSide(pointer);
    }

    private bool TryCenterDrop(Tile tile, Point pointer, out bool forceMaster)
    {
        forceMaster = false;
        var master = FirstMaster();
        if (master is null || ReferenceEquals(master, tile))
            return false;

        var box = master.Box;
        if (pointer.X >= box.X && pointer.X <= box.X + box.Width)
        {
            forceMaster = true;
            return true;
        }

        var dropRight = pointer.X > box.X + (box.Width / 2.0);
        var firstSideRight = _master.CenterFallback == MasterOrientation.Right;
        _tiles.Remove(tile);

        var slot = 0;
        var slaves = 0;
        foreach (var node in _tiles)
        {
            if (node.Master)
                continue;

            var nodeRight = (slaves % 2 == 0) == firstSideRight;
            if (nodeRight == dropRight && pointer.Y > node.Box.Y + (node.Box.Height / 2.0))
                slot++;

            slaves++;
        }

        var targetFirst = dropRight == firstSideRight;
        var totalSlaves = slaves + 1;
        var cap = targetFirst ? (totalSlaves + 1) / 2 : totalSlaves / 2;
        if (cap > 0)
            slot = Math.Min(slot, cap - 1);

        var slavePos = cap == 0 ? 0 : targetFirst ? (2 * slot) : (2 * slot) + 1;
        var insert = _tiles.Count;
        var counted = 0;
        for (var i = 0; i < _tiles.Count; i++)
        {
            if (counted == slavePos)
            {
                insert = i;
                break;
            }

            if (!_tiles[i].Master)
                counted++;
        }

        _tiles.Insert(insert, tile);
        return true;
    }

    private void ReorderToCursor(Tile tile, Point pointer)
    {
        var source = _tiles.IndexOf(tile);
        if (source < 0)
            return;

        var orientation = Dynamic();
        for (var i = 0; i < _tiles.Count; i++)
        {
            if (ReferenceEquals(_tiles[i], tile) || !Contains(_tiles[i].Box, pointer))
                continue;

            var insert = i;
            if (orientation is MasterOrientation.Left or MasterOrientation.Right)
            {
                if (pointer.Y > _tiles[i].Box.Y + (_tiles[i].Box.Height / 2.0))
                    insert++;
            }
            else if (orientation is MasterOrientation.Top or MasterOrientation.Bottom)
            {
                if (pointer.X > _tiles[i].Box.X + (_tiles[i].Box.Width / 2.0))
                    insert++;
            }

            if (insert > source)
                insert--;
            if (insert == source)
                return;

            _tiles.RemoveAt(source);
            _tiles.Insert(Math.Clamp(insert, 0, _tiles.Count), tile);
            return;
        }
    }

    private bool CursorOnMasterSide(Point pointer)
    {
        var master = FirstMaster();
        if (master is null)
            return false;

        var box = master.Box;
        var midX = box.X + (box.Width / 2.0);
        var midY = box.Y + (box.Height / 2.0);
        return Dynamic() switch
        {
            MasterOrientation.Right => pointer.X > midX,
            MasterOrientation.Top => pointer.Y < midY,
            MasterOrientation.Bottom => pointer.Y > midY,
            _ => pointer.X < midX,
        };
    }

    private void AssignRole(Tile tile, bool firstMap, bool dragging, bool forceMaster)
    {
        var fraction = _master.Mfact;
        var focused = Find(_focused);
        var opening = focused ?? FirstMaster();
        var makeMaster = forceMaster
            || _master.NewStatus == MasterNewStatus.Master
            || _tiles.Count == 1
            || (_tiles.Count > 2 && !firstMap && focused is { Master: true } && !ReferenceEquals(focused, tile))
            || (_master.NewStatus == MasterNewStatus.Inherit && !dragging && opening is { Master: true } && !ReferenceEquals(opening, tile));

        if (!makeMaster)
        {
            tile.Master = false;
            tile.MasterFraction = fraction;
            return;
        }

        if (_master.NewOnActive == MasterNewOnActive.Before)
        {
            for (var i = _tiles.Count - 1; i >= 0; i--)
            {
                if (_tiles[i].Master && !ReferenceEquals(_tiles[i], tile))
                {
                    _tiles[i].Master = false;
                    fraction = _tiles[i].MasterFraction;
                    break;
                }
            }
        }
        else
        {
            foreach (var node in _tiles)
            {
                if (node.Master && !ReferenceEquals(node, tile))
                {
                    node.Master = false;
                    fraction = node.MasterFraction;
                    break;
                }
            }
        }

        tile.Master = true;
        tile.MasterFraction = fraction;
    }

    private void Detach(Tile tile)
    {
        var masters = MasterCount();
        if (tile.Master && (masters <= 1 || _master.AllowSmallSplit))
        {
            foreach (var other in _tiles)
            {
                if (!other.Master)
                {
                    other.Master = true;
                    other.MasterFraction = tile.MasterFraction;
                    break;
                }
            }
        }

        _tiles.Remove(tile);
        if (MasterCount() == _tiles.Count && masters > 1)
        {
            for (var i = _tiles.Count - 1; i >= 0; i--)
            {
                if (_tiles[i].Master)
                {
                    _tiles[i].Master = false;
                    break;
                }
            }
        }

        if (_tiles.Count == 1)
            _tiles[0].Master = true;

        Recalculate();
    }

    private void Recalculate()
    {
        var master = FirstMaster();
        if (master is null)
            return;

        var area = TilingArea();
        if (area.Width <= 0 || area.Height <= 0)
        {
            foreach (var tile in _tiles)
                tile.Box = area;
            return;
        }

        var orientation = Resolve(out var centered);
        var slaves = SlaveCount();
        if (_tiles.Count == 1 && !centered)
        {
            PlaceAlone(master, area, orientation);
            return;
        }

        if (orientation is MasterOrientation.Top or MasterOrientation.Bottom)
            PlaceBands(area, orientation, master, slaves);
        else
            PlaceColumns(area, orientation, master, slaves, centered);
    }

    private void PlaceAlone(Tile master, Rectangle area, MasterOrientation orientation)
    {
        if (!_master.AlwaysKeepPosition)
            master.Box = area;
        else
        {
            var width = Share(area.Width, master.MasterFraction);
            var x = orientation switch
            {
                MasterOrientation.Right => area.Width - width,
                MasterOrientation.Center => (area.Width - width) / 2,
                _ => 0,
            };
            master.Box = new Rectangle(area.X + x, area.Y, width, area.Height);
        }

        // One tile has no neighbour, so the box may extend past the work area until the
        // presented size meets the window's minimum.
        GrowToMinimum(master);
    }

    private void PlaceBands(Rectangle area, MasterOrientation orientation, Tile master, int slaves)
    {
        var masterHeight = slaves > 0 ? Share(area.Height, master.MasterFraction) : area.Height;
        var masterY = orientation == MasterOrientation.Bottom ? area.Y + area.Height - masterHeight : area.Y;
        PlaceStack(Masters(), area.X, masterY, area.Width, masterHeight, vertical: false);
        if (slaves == 0)
            return;

        var slaveHeight = area.Height - masterHeight;
        var slaveY = orientation == MasterOrientation.Top ? masterY + masterHeight : area.Y;
        PlaceStack(Slaves(), area.X, slaveY, area.Width, slaveHeight, vertical: false);
    }

    private void PlaceColumns(Rectangle area, MasterOrientation orientation, Tile master, int slaves, bool centered)
    {
        var masterWidth = area.Width;
        if (slaves > 0 || centered)
            masterWidth = Share(area.Width, master.MasterFraction);

        var masterX = area.X;
        if (orientation == MasterOrientation.Right)
            masterX = area.X + area.Width - masterWidth;
        else if (centered)
            masterX = area.X + ((area.Width - masterWidth) / 2);

        PlaceStack(Masters(), masterX, area.Y, masterWidth, area.Height, vertical: true);
        if (slaves == 0)
            return;

        if (centered)
        {
            PlaceCenterSlaves(area, masterX, masterWidth);
            return;
        }

        var slaveWidth = area.Width - masterWidth;
        var slaveX = orientation == MasterOrientation.Right ? area.X : masterX + masterWidth;
        PlaceStack(Slaves(), slaveX, area.Y, slaveWidth, area.Height, vertical: true);
    }

    private void PlaceCenterSlaves(Rectangle area, int masterX, int masterWidth)
    {
        var extraRight = _master.CenterFallback == MasterOrientation.Right;
        var left = new List<Tile>();
        var right = new List<Tile>();
        var onRight = extraRight;
        foreach (var slave in Slaves())
        {
            (onRight ? right : left).Add(slave);
            onRight = !onRight;
        }

        var leftWidth = masterX - area.X;
        var rightX = masterX + masterWidth;
        var rightWidth = area.X + area.Width - rightX;
        PlaceStack(left, area.X, area.Y, leftWidth, area.Height, vertical: true);
        PlaceStack(right, rightX, area.Y, rightWidth, area.Height, vertical: true);
    }

    private void PlaceStack(List<Tile> tiles, int x, int y, int width, int height, bool vertical)
    {
        if (tiles.Count == 0 || width < 0 || height < 0)
            return;

        var total = vertical ? height : width;
        var sizes = Sizes(tiles, total);
        // The stack honors a window's minimum on this axis when the minimums fit.
        // The master/slave split owns the other axis, so a minimum wider than the column
        // is not applied there. When the minimums sum to more than the stack, the
        // proportional sizes stay and the stack does not grow past its column.
        RaiseToMinimum(tiles, sizes, total, vertical, vertical ? y : x);
        var cursor = 0;
        for (var i = 0; i < tiles.Count; i++)
        {
            var size = sizes[i];
            tiles[i].Box = vertical
                ? new Rectangle(x, y + cursor, width, size)
                : new Rectangle(x + cursor, y, size, height);
            cursor += size;
        }
    }

    private int[] Sizes(List<Tile> tiles, int total)
    {
        var count = tiles.Count;
        var sizes = new int[count];
        if (count == 0 || total <= 0)
            return sizes;

        if (_master.SmartResizing)
        {
            var accumulated = 0.0;
            foreach (var tile in tiles)
                accumulated += total / (double)count * tile.SizeFraction;

            if (accumulated > 0)
            {
                var scale = total / accumulated;
                foreach (var tile in tiles)
                    tile.SizeFraction *= scale;
            }

            var average = total / (double)count;
            var cursor = 0.0;
            for (var i = 0; i < count; i++)
            {
                var start = Round(cursor);
                cursor += average * tiles[i].SizeFraction;
                var end = i == count - 1 ? total : Math.Clamp(Round(cursor), start, total);
                sizes[i] = end - start;
            }

            return sizes;
        }

        var remaining = (double)total;
        var left = count;
        var sequential = 0.0;
        for (var i = 0; i < count; i++)
        {
            double size;
            if (left <= 1)
                size = remaining;
            else
            {
                size = remaining / left * tiles[i].SizeFraction;
                if (size > remaining * 0.9)
                    size = remaining * 0.9;
            }

            var start = Round(sequential);
            sequential += size;
            var end = i == count - 1 ? total : Math.Clamp(Round(sequential), start, total);
            sizes[i] = end - start;
            remaining -= size;
            left--;
        }

        return sizes;
    }

    private MasterOrientation Resolve(out bool centered)
    {
        centered = false;
        var orientation = Dynamic();
        if (orientation != MasterOrientation.Center)
            return orientation;

        if (SlaveCount() >= _master.SlaveCountForCenter)
        {
            centered = true;
            return MasterOrientation.Center;
        }

        return _master.CenterFallback == MasterOrientation.Center
            ? MasterOrientation.Left
            : _master.CenterFallback;
    }

    private MasterOrientation Dynamic() => _explicit ?? _master.Orientation;

    private Rectangle Present(Rectangle box)
    {
        var area = TilingArea();
        var gaps = _layout.GapsIn;
        var left = box.X == area.X ? 0 : gaps.Left;
        var top = box.Y == area.Y ? 0 : gaps.Top;
        var right = box.X + box.Width == area.X + area.Width ? 0 : gaps.Right;
        var bottom = box.Y + box.Height == area.Y + area.Height ? 0 : gaps.Bottom;
        var presented = Shrink(box, left, top, right, bottom);
        var border = _layout.BorderSize;
        return Shrink(presented, border, border, border, border);
    }

    private Rectangle TilingArea()
    {
        var gaps = _layout.GapsOut;
        var width = Math.Max(0, _area.Width - gaps.Left - gaps.Right);
        var height = Math.Max(0, _area.Height - gaps.Top - gaps.Bottom);
        return new Rectangle(_area.X + gaps.Left, _area.Y + gaps.Top, width, height);
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

    private static int Share(int total, double fraction)
    {
        var size = Round(total * fraction);
        if (size < 0)
            return 0;
        return size > total ? total : size;
    }

    private static int Round(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static Rectangle Shrink(Rectangle box, int left, int top, int right, int bottom)
    {
        var width = Math.Max(0, box.Width - left - right);
        var height = Math.Max(0, box.Height - top - bottom);
        return new Rectangle(box.X + left, box.Y + top, width, height);
    }

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

    private void RaiseToMinimum(List<Tile> tiles, int[] sizes, int total, bool vertical, int origin)
    {
        var count = tiles.Count;
        if (count == 0 || sizes.Length != count || total <= 0)
            return;

        var area = TilingArea();
        var areaStart = vertical ? area.Y : area.X;
        var areaSpan = vertical ? area.Height : area.Width;
        var needs = new int[count];
        long sum = 0;
        for (var i = 0; i < count; i++)
        {
            var atStart = i == 0 && origin == areaStart;
            var atEnd = i == count - 1 && origin + total == areaStart + areaSpan;
            needs[i] = AlongNeed(tiles[i], vertical, atStart, atEnd);
            sum += needs[i];
        }

        if (sum > total)
            return;

        var extra = total - (int)sum;
        var flex = 0;
        for (var i = 0; i < count; i++)
            flex += Math.Max(0, sizes[i] - needs[i]);

        var cursor = 0;
        for (var i = 0; i < count; i++)
        {
            int size;
            if (i == count - 1)
                size = total - cursor;
            else if (flex == 0)
                size = needs[i];
            else
            {
                var share = (int)((long)extra * Math.Max(0, sizes[i] - needs[i]) / flex);
                size = needs[i] + share;
            }

            sizes[i] = Math.Max(0, size);
            cursor += sizes[i];
        }
    }

    private int AlongNeed(Tile tile, bool vertical, bool atStart, bool atEnd)
    {
        if (!_limits.TryGetValue(tile.Id, out var limits) || limits.Min is not { } min)
            return 0;

        var want = vertical ? min.Height : min.Width;
        if (want < 1)
            return 0;

        var gaps = _layout.GapsIn;
        var before = atStart ? 0 : vertical ? gaps.Top : gaps.Left;
        var after = atEnd ? 0 : vertical ? gaps.Bottom : gaps.Right;
        var chrome = before + after + (_layout.BorderSize * 2);
        var need = (long)want + chrome;
        return need > int.MaxValue ? int.MaxValue : (int)need;
    }

    private void GrowToMinimum(Tile tile)
    {
        if (!_limits.TryGetValue(tile.Id, out var limits) || limits.Min is not { } min)
            return;

        var area = TilingArea();
        var presented = Present(tile.Box);
        var box = tile.Box;
        if (min.Width > presented.Width)
        {
            var added = min.Width - presented.Width;
            if (box.X + box.Width == area.X + area.Width)
                added += _layout.GapsIn.Right;

            box = box with { Width = box.Width + added };
        }

        if (min.Height > presented.Height)
        {
            var added = min.Height - presented.Height;
            if (box.Y + box.Height == area.Y + area.Height)
                added += _layout.GapsIn.Bottom;

            box = box with { Height = box.Height + added };
        }

        tile.Box = box;
    }

    private static Client Adopted(
        ClientId id,
        WindowId window,
        WorkspaceId workspace,
        string windowClass,
        string title,
        bool floating,
        long processId,
        string initialClass,
        string initialTitle) =>
        new(
            id,
            window,
            Floating: floating,
            Workspace: workspace,
            Class: windowClass,
            Title: title,
            ProcessId: processId,
            InitialClass: initialClass.Length == 0 ? windowClass : initialClass,
            InitialTitle: initialTitle.Length == 0 ? title : initialTitle);

    private static Rectangle ClampToLimits(Rectangle bounds, Size? min, Size? max)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        if (max is { } hi)
        {
            if (hi.Width < int.MaxValue && width > hi.Width)
                width = hi.Width;
            if (hi.Height < int.MaxValue && height > hi.Height)
                height = hi.Height;
        }

        if (min is { } lo)
        {
            if (lo.Width > 0 && width < lo.Width)
                width = lo.Width;
            if (lo.Height > 0 && height < lo.Height)
                height = lo.Height;
        }

        if (width == bounds.Width && height == bounds.Height)
            return bounds;

        return new Rectangle(bounds.X, bounds.Y, width, height);
    }

    private sealed class Tile
    {
        public ClientId Id { get; set; }

        public bool Master { get; set; }

        public double MasterFraction { get; set; } = 0.55;

        public double SizeFraction { get; set; } = 1;

        public Rectangle Box { get; set; }
    }

    private sealed record Suspended(Tile Tile, int Index);

    private readonly record struct ClientLimits(Size? Min, Size? Max);
}
