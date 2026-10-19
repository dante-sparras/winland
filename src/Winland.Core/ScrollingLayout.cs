using System.Globalization;

namespace Winland;

/// <summary>
/// Columns on a tape. The viewport shows a slice of the tape.
/// A column whose box misses the viewport is left out of the placements, so the caller conceals it.
/// A column that only partly overlaps keeps its full tape rectangle and hangs off the edge.
/// </summary>
internal sealed partial class ScrollingLayout : Layout
{
    private const double MinRow = 0.1;
    private const double MaxRow = 1;

    private readonly List<Client> _clients = [];
    private readonly Dictionary<ClientId, Cell> _cells = [];
    private readonly Dictionary<ClientId, Suspension> _suspended = [];
    private readonly FloatingClients _floating = new();
    private readonly Dictionary<ClientId, Rectangle> _natural = [];
    private readonly Dictionary<ClientId, SizeLimits> _limits = [];
    private readonly Dictionary<WindowId, double> _widths = [];
    private readonly List<Column> _columns = [];
    private LayoutOptions _layoutOptions = LayoutOptions.Unconfigured;
    private ScrollingOptions _scrolling = ScrollingOptions.Defaults;
    private Rectangle _area;
    private Rectangle _monitor;
    private WindowId? _focus;
    private double _offset;
    private bool _inhibited;
    private double _inhibitedOffset;
    private bool _dragging;

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

    public void Configure(ScrollingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _scrolling = options;
        Recalculate();
    }

    public void NoteScrollingWidth(WindowId window, double width)
    {
        if (double.IsFinite(width) && width > 0)
            _widths[window] = width;
    }

    public IReadOnlyList<Placement> Apply(LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _layoutOptions = options;
        Recalculate();
        return Placements();
    }

    public void Fit(Rectangle area)
    {
        // A new work area means the workspace moved or its monitor changed, so the
        // monitor rectangle from the last Add is stale. Cull against the work area
        // until the next Add brings the current monitor.
        if (area != _area)
            _monitor = default;
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
        _ = minSize;
        _ = maxSize;
        _ = activeWorkspace;
        _area = area;
        if (monitor.Width > 0 && monitor.Height > 0)
            _monitor = monitor;

        var id = new ClientId(window.Value);
        if (_cells.ContainsKey(id) || _suspended.ContainsKey(id) || _floating.Contains(id))
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

        InsertTiled(id, window);
        _focus = window;
        Recalculate();
        return Placements();
    }

    public IReadOnlyList<Placement> Remove(WindowId window)
    {
        var id = new ClientId(window.Value);
        _natural.Remove(id);
        _limits.Remove(id);
        _widths.Remove(window);
        _restoreWidths.Remove(id);
        var suspended = _suspended.Remove(id);
        if (_focus == window)
            _focus = null;
        if (_drag.Forget(id))
            _dragging = false;

        if (_floating.Forget(id) || suspended)
        {
            _clients.RemoveAll(client => client.Id == id);
            return [];
        }

        if (!_cells.Remove(id, out var cell))
            return [];

        _clients.RemoveAll(client => client.Id == id);
        RemoveCell(cell);
        Recalculate();
        return Placements();
    }

    public IReadOnlyList<Placement> Minimize(WindowId window)
    {
        var id = new ClientId(window.Value);
        if (_floating.Contains(id))
        {
            SetMinimized(id, true);
            return [];
        }

        if (!_cells.Remove(id, out var cell) || cell.Column is not Column column)
            return [];

        var columnIndex = _columns.IndexOf(column);
        var cellIndex = column.Cells.IndexOf(cell);
        SetMinimized(id, true);
        RemoveCell(cell);
        _suspended[id] = new Suspension(column, columnIndex, cellIndex, cell.Size);
        Recalculate();
        return Placements();
    }

    public IReadOnlyList<Placement> Restore(WindowId window)
    {
        var id = new ClientId(window.Value);
        if (_floating.TryGet(id, out var bounds))
        {
            var index = _clients.FindIndex(client => client.Id == id);
            if (index < 0 || !_clients[index].Minimized)
                return [];

            SetMinimized(id, false);
            return [new Placement(id, bounds)];
        }

        if (!_suspended.Remove(id, out var suspension))
            return [];

        SetMinimized(id, false);
        var cell = new Cell(id) { Size = suspension.Size };
        _cells[id] = cell;
        var column = suspension.Column;
        if (_columns.Contains(column))
            AddCell(column, cell, Math.Max(0, suspension.CellIndex) - 1);
        else
        {
            if (column.Cells.Count == 0)
                cell.Size = 1;
            AddCell(column, cell);
            var index = Math.Clamp(suspension.ColumnIndex, 0, _columns.Count);
            _columns.Insert(index, column);
        }

        _focus = window;
        Recalculate();
        return Placements();
    }

    public bool TryTake(WindowId window, out Taken taken)
    {
        taken = default;
        var id = new ClientId(window.Value);
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0)
            return false;

        var client = _clients[index];
        _limits.TryGetValue(id, out var limits);
        var bounds = _floating.TryGet(id, out var floating)
            ? floating
            : _natural.TryGetValue(id, out var natural) ? natural : default;
        double? width = _cells.TryGetValue(id, out var cell) && cell.Column is Column column
            ? column.Width
            : _suspended.TryGetValue(id, out var suspension) ? suspension.Column.Width : null;
        taken = SlotHandover.Describe(client, bounds, limits, width);
        Remove(window);
        return true;
    }

    // Column widths come from the tape, so a window's size limits do not change a tiled placement here.
    // They are kept so a later layout can still honor them. A floating Client is clamped to them.
    public IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize, TiledLimits? tiledLimits = null)
    {
        var id = new ClientId(window.Value);
        if (_clients.FindIndex(client => client.Id == id) < 0)
            return [];

        _limits[id] = new SizeLimits(minSize, maxSize) { Tiled = tiledLimits };
        return _floating.Clamp(id, new SizeLimits(minSize, maxSize));
    }

    public bool TryBounds(WindowId window, out Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        if (_floating.TryGet(id, out bounds))
            return true;

        if (_cells.TryGetValue(id, out var cell) && cell.Logical.Width > 0 && cell.Logical.Height > 0)
        {
            bounds = cell.Logical;
            return true;
        }

        bounds = default;
        return false;
    }

    public IReadOnlyList<Placement> Visible()
    {
        var placements = new List<Placement>(Placements());
        _floating.AddShown(placements, _clients);
        return placements;
    }

    public IReadOnlyList<Placement> Placed() => Placements();

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

    public void NoteFocus(WindowId window) => Follow(window, pointer: false);

    public void NotePointerFocus(WindowId window) => Follow(window, pointer: true);

    // The strip follows the focus with scrolling:follow_focus. As in the pinned release, scrolling:follow_min_visible
    // holds it back only for focus the pointer gives, so a dispatcher, a click or a Group change always follows.
    private void Follow(WindowId window, bool pointer)
    {
        if (CellFor(window) is not Cell cell || cell.Column is not Column column)
            return;

        _focus = window;
        column.LastFocused = cell;
        if (!_scrolling.FollowFocus || ColumnVisible(column, full: true))
            return;

        var view = Viewport();
        var span = IsHorizontal() ? view.Width : view.Height;
        if (pointer && _scrolling.FollowMinVisible > 0 && VisibleLength(cell) < span * _scrolling.FollowMinVisible)
            return;

        CenterOrFit(column);
        Recalculate();
        _moved = true;
    }

    // The strip scrolled because focus moved, and its placements have not been sent yet.
    private bool _moved;

    public bool TakeMoved()
    {
        var moved = _moved;
        _moved = false;
        return moved;
    }

    private Placement[] Placements()
    {
        var placements = new List<Placement>();
        var view = Viewport();
        foreach (var client in _clients)
        {
            if (client.Minimized || client.Floating)
                continue;
            if (!_cells.TryGetValue(client.Id, out var cell))
                continue;

            var box = cell.Logical;
            if (!Intersects(box, view))
                continue;

            placements.Add(new Placement(client.Id, box));
        }

        return placements.ToArray();
    }

    private Cell? CellFor(WindowId window) =>
        _cells.TryGetValue(new ClientId(window.Value), out var cell) ? cell : null;

    private Column? FocusColumn() =>
        _focus is WindowId window && CellFor(window) is Cell cell ? cell.Column : null;

    private void SetMinimized(ClientId id, bool minimized)
    {
        var index = _clients.FindIndex(client => client.Id == id);
        if (index >= 0)
            _clients[index] = _clients[index] with { Minimized = minimized };
    }

    private void SetFloating(ClientId id, bool floating)
    {
        var index = _clients.FindIndex(client => client.Id == id);
        if (index >= 0)
            _clients[index] = _clients[index] with { Floating = floating };
    }

    private static bool Intersects(Rectangle box, Rectangle view) =>
        box.Width > 0
        && box.Height > 0
        && view.Width > 0
        && view.Height > 0
        && box.X < view.X + view.Width
        && view.X < box.X + box.Width
        && box.Y < view.Y + view.Height
        && view.Y < box.Y + box.Height;

    private static Rectangle Pixels(double x, double y, double width, double height)
    {
        var left = Round(x);
        var top = Round(y);
        var right = Round(x + width);
        var bottom = Round(y + height);
        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static int Round(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static string[] Tokens(string arguments) =>
        arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static string At(string[] tokens, int index) =>
        index < tokens.Length ? tokens[index] : "";

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static bool TryRelative(string text, out double value)
    {
        value = 0;
        if (text.Length < 2 || (text[0] != '+' && text[0] != '-'))
            return false;
        return TryDouble(text, out value);
    }

    private void Fail(string message) => LayoutMessageError = "error: " + message;

    private sealed class Column(double width)
    {
        public double Width { get; set; } = width;

        public List<Cell> Cells { get; } = [];

        public Cell? LastFocused { get; set; }
    }

    private sealed class Cell(ClientId id)
    {
        public ClientId Id { get; set; } = id;

        public Column? Column { get; set; }

        public double Size { get; set; } = 1;

        public Rectangle Box { get; set; }

        public Rectangle Logical { get; set; }
    }

    private sealed class Suspension(Column column, int columnIndex, int cellIndex, double size)
    {
        public Column Column { get; } = column;

        public int ColumnIndex { get; } = columnIndex;

        public int CellIndex { get; } = cellIndex;

        public double Size { get; } = size;
    }
}
