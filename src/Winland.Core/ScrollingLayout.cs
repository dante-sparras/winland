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
    private readonly Dictionary<ClientId, Rectangle> _floating = [];
    private readonly Dictionary<ClientId, Rectangle> _natural = [];
    private readonly Dictionary<ClientId, (Size? Min, Size? Max)> _limits = [];
    private readonly Dictionary<ClientId, (int Width, int Height)> _lastFloat = [];
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
    private MouseGrab? _grab;

    public IReadOnlyList<Client> Clients => _clients;

    public bool FollowsMonitorWhenUnmoved => true;

    public void NudgeFloating(int dx, int dy) => FloatingShift.By(_floating, dx, dy);

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
        _ = minSize;
        _ = maxSize;
        _ = activeWorkspace;
        _area = area;
        if (monitor.Width > 0 && monitor.Height > 0)
            _monitor = monitor;

        var id = new ClientId(window.Value);
        if (_cells.ContainsKey(id) || _suspended.ContainsKey(id) || _floating.ContainsKey(id))
            return [];

        _natural[id] = bounds;
        _limits[id] = (minSize, maxSize);
        if (bounds.Width > 0 && bounds.Height > 0)
            _lastFloat[id] = (bounds.Width, bounds.Height);

        _clients.Add(new Client(
            id,
            window,
            floating,
            Workspace: workspace,
            Class: windowClass,
            Title: title,
            ProcessId: processId,
            InitialClass: initialClass.Length == 0 ? windowClass : initialClass,
            InitialTitle: initialTitle.Length == 0 ? title : initialTitle));
        if (floating)
        {
            _floating[id] = bounds;
            return [new Placement(id, bounds)];
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
        _lastFloat.Remove(id);
        _widths.Remove(window);
        _suspended.Remove(id);
        if (_focus == window)
            _focus = null;

        if (_floating.Remove(id))
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
        if (_floating.ContainsKey(id))
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
        if (_floating.TryGetValue(id, out var bounds))
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
        var bounds = _floating.TryGetValue(id, out var floating)
            ? floating
            : _natural.TryGetValue(id, out var natural) ? natural : default;
        double? width = _cells.TryGetValue(id, out var cell) && cell.Column is Column column
            ? column.Width
            : _suspended.TryGetValue(id, out var suspension) ? suspension.Column.Width : null;
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
            limits.Max,
            width);
        Remove(window);
        return true;
    }

    // Column widths come from the tape, so a window's size limits do not change the placement here.
    // They are kept so a later layout can still honor them.
    public IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize)
    {
        var id = new ClientId(window.Value);
        if (_clients.FindIndex(client => client.Id == id) < 0)
            return [];

        _limits[id] = (minSize, maxSize);
        return [];
    }

    public bool TryBounds(WindowId window, out Rectangle bounds)
    {
        var id = new ClientId(window.Value);
        if (_floating.TryGetValue(id, out bounds))
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
        foreach (var client in _clients)
        {
            if (!client.Floating || client.Minimized)
                continue;
            if (_floating.TryGetValue(client.Id, out var bounds))
                placements.Add(new Placement(client.Id, bounds));
        }

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

    public void NoteFocus(WindowId window)
    {
        if (CellFor(window) is not Cell cell || cell.Column is not Column column)
            return;

        _focus = window;
        column.LastFocused = cell;
        if (!_scrolling.FollowFocus || ColumnVisible(column, full: true))
            return;

        var view = Viewport();
        var span = IsHorizontal() ? view.Width : view.Height;
        if (_scrolling.FollowMinVisible > 0 && VisibleLength(cell) < span * _scrolling.FollowMinVisible)
            return;

        CenterOrFit(column);
        Recalculate();
    }

    public IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode)
    {
        if (!FullscreenClients.Assign(_clients, window, mode, clientMode))
            return [];

        var placements = new List<Placement>(Visible());
        var id = new ClientId(window.Value);
        if (mode != FullscreenMode.None && !placements.Exists(placement => placement.Client == id) && TryBounds(window, out var bounds))
            placements.Add(new Placement(id, bounds));

        return placements;
    }

    /// <summary>
    /// The tape keeps this Client's slot. A covering rectangle is applied outside the layout.
    /// </summary>
    internal Rectangle? FullscreenPlacement(ClientId client) => null;

    private void InsertTiled(ClientId id, WindowId window)
    {
        var cell = new Cell(id);
        _cells[id] = cell;
        var width = TakeWidth(window);
        var focus = FocusColumn();
        var column = focus is null ? Append(width) : InsertAfter(focus, width);
        AddCell(column, cell);
        column.LastFocused = cell;
        FitColumn(column);
    }

    private double? TakeWidth(WindowId window)
    {
        if (!_widths.Remove(window, out var width))
            return null;

        return Math.Clamp(width, ScrollingOptions.MinColumnWidth, ScrollingOptions.MaxColumnWidth);
    }

    private Column Append(double? width)
    {
        var column = NewColumn(width);
        _columns.Add(column);
        return column;
    }

    private Column InsertAfter(Column after, double? width)
    {
        var column = NewColumn(width);
        var index = _columns.IndexOf(after);
        if (index < 0 || index + 1 >= _columns.Count)
            _columns.Add(column);
        else
            _columns.Insert(index + 1, column);
        return column;
    }

    private Column InsertAt(int index, double? width)
    {
        var column = NewColumn(width);
        _columns.Insert(Math.Clamp(index, 0, _columns.Count), column);
        return column;
    }

    private Column NewColumn(double? width) => new(width ?? _scrolling.ColumnWidth);

    private static void AddCell(Column column, Cell cell, int? after = null)
    {
        var count = column.Cells.Count;
        var next = count + 1;
        var scale = count / (double)next;
        foreach (var existing in column.Cells)
            existing.Size *= scale;

        cell.Column = column;
        cell.Size = 1.0 / next;
        if (after is int slot)
            column.Cells.Insert(Math.Clamp(slot + 1, 0, column.Cells.Count), cell);
        else
            column.Cells.Add(cell);
    }

    private void RemoveCell(Cell cell)
    {
        if (cell.Column is not Column column)
            return;

        var alone = column.Cells.Count <= 1;
        var last = _columns.Count > 0 && ReferenceEquals(_columns[^1], column);
        if (alone && last)
            _offset -= PrimaryLength() * column.Width;

        column.Cells.Remove(cell);
        cell.Column = null;
        if (ReferenceEquals(column.LastFocused, cell))
            column.LastFocused = column.Cells.Count == 0 ? null : column.Cells[0];

        if (column.Cells.Count == 0)
        {
            _columns.Remove(column);
            var limit = Math.Max(MaxExtent() - PrimaryLength(), 1);
            SetOffset(Math.Clamp(_offset, 0, limit));
            return;
        }

        var sum = 0.0;
        foreach (var item in column.Cells)
            sum += item.Size;
        if (sum <= 0)
            return;

        foreach (var item in column.Cells)
            item.Size /= sum;
    }

    private void Recalculate()
    {
        var usable = Usable();
        if (usable.Width <= 0 || usable.Height <= 0 || _columns.Count == 0)
            return;

        var horizontal = IsHorizontal();
        var primary = horizontal ? usable.Width : usable.Height;
        var secondary = horizontal ? usable.Height : usable.Width;
        var fullscreen = OneColumn();
        var camera = Camera(primary, MaxExtent(primary, fullscreen));
        var reversed = IsReversed();
        var border = _layoutOptions.BorderSize;

        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var start = 0.0;
            for (var earlier = 0; earlier < i; earlier++)
                start += StripSize(earlier, primary, fullscreen);

            var size = StripSize(i, primary, fullscreen);
            var primaryPos = reversed ? primary - start - size + camera : start - camera;
            var cursor = 0.0;
            for (var j = 0; j < column.Cells.Count; j++)
            {
                var cell = column.Cells[j];
                var span = cell.Size * secondary;
                if (FullscreenPlacement(cell.Id) is Rectangle covering)
                {
                    cell.Logical = covering;
                    cell.Box = covering;
                    cursor += span;
                    continue;
                }

                double x;
                double y;
                double width;
                double height;
                if (horizontal)
                {
                    x = usable.X + primaryPos;
                    y = usable.Y + cursor;
                    width = size;
                    height = span;
                }
                else
                {
                    x = usable.X + cursor;
                    y = usable.Y + primaryPos;
                    width = span;
                    height = size;
                }

                var gaps = GapsFor(i, j, column.Cells.Count, horizontal);
                x += gaps.Left;
                y += gaps.Top;
                width = Math.Max(1, width - gaps.Left - gaps.Right);
                height = Math.Max(1, height - gaps.Top - gaps.Bottom);
                x += border;
                y += border;
                width = Math.Max(0, width - (border * 2.0));
                height = Math.Max(0, height - (border * 2.0));
                var box = Pixels(x, y, width, height);
                cell.Logical = box;
                cell.Box = box;
                cursor += span;
            }
        }
    }

    private (int Left, int Top, int Right, int Bottom) GapsFor(int column, int cell, int cells, bool horizontal)
    {
        var gaps = _layoutOptions.GapsIn;
        var reversed = IsReversed();
        var notBefore = reversed ? column + 1 < _columns.Count : column > 0;
        var notAfter = reversed ? column > 0 : column + 1 < _columns.Count;
        var notAbove = reversed ? cell + 1 < cells : cell > 0;
        var notBelow = reversed ? cell > 0 : cell + 1 < cells;
        var left = horizontal ? notBefore : notAbove;
        var right = horizontal ? notAfter : notBelow;
        var top = horizontal ? notAbove : notBefore;
        var bottom = horizontal ? notBelow : notAfter;
        return (left ? gaps.Left : 0, top ? gaps.Top : 0, right ? gaps.Right : 0, bottom ? gaps.Bottom : 0);
    }

    private double Camera(double primary, double max)
    {
        if (_dragging)
            return _offset;

        if (max < primary)
            SetOffset(Math.Round((max - primary) / 2.0, MidpointRounding.AwayFromZero));
        if (max > primary && _offset < 0 && _scrolling.FocusFitMethod != ScrollingFit.Center)
            SetOffset(0);
        return _offset;
    }

    private void FitColumn(Column column)
    {
        if (!TryStrip(column, out var start, out var size, out var primary))
            return;

        var lo = start - primary + size;
        var hi = start;
        if (lo > hi)
            SetOffset(start - ((primary - size) / 2.0));
        else
            SetOffset(Math.Clamp(_offset, lo, hi));
    }

    private void CenterColumn(Column column)
    {
        if (!TryStrip(column, out var start, out var size, out var primary))
            return;

        SetOffset(start - ((primary - size) / 2.0));
    }

    private void CenterOrFit(Column? column)
    {
        if (column is null)
            return;

        if (_scrolling.FocusFitMethod == ScrollingFit.Fit)
            FitColumn(column);
        else
            CenterColumn(column);
    }

    private bool TryStrip(Column column, out double start, out double size, out double primary)
    {
        start = 0;
        size = 0;
        primary = PrimaryLength();
        var index = _columns.IndexOf(column);
        if (index < 0 || primary <= 0)
            return false;

        var fullscreen = OneColumn();
        for (var i = 0; i < index; i++)
            start += StripSize(i, primary, fullscreen);
        size = StripSize(index, primary, fullscreen);
        return true;
    }

    private double StripSize(int index, double primary, bool fullscreen) =>
        fullscreen ? primary : primary * _columns[index].Width;

    private bool OneColumn() => _scrolling.FullscreenOnOneColumn && _columns.Count == 1;

    private double MaxExtent() => MaxExtent(PrimaryLength(), OneColumn());

    private double MaxExtent(double primary, bool fullscreen)
    {
        if (_columns.Count == 0 || primary <= 0)
            return 0;
        if (fullscreen)
            return primary;

        var total = 0.0;
        foreach (var column in _columns)
            total += primary * column.Width;
        return total;
    }

    private bool ColumnVisible(Column column, bool full)
    {
        if (!TryStrip(column, out var start, out var size, out var primary))
            return false;

        var end = start + size;
        var viewEnd = _offset + primary;
        if (!full)
            return start < viewEnd && _offset < end;
        return start >= _offset && end <= viewEnd;
    }

    private Column? ColumnAtCenter()
    {
        if (_columns.Count == 0)
            return null;

        var primary = PrimaryLength();
        var fullscreen = OneColumn();
        var center = _offset + (primary / 2.0);
        var end = 0.0;
        for (var i = 0; i < _columns.Count; i++)
        {
            end += StripSize(i, primary, fullscreen);
            if (end >= center - 2)
                return _columns[i];
        }

        return _columns[^1];
    }

    private void SetOffset(double offset)
    {
        _offset = _inhibited ? _inhibitedOffset : offset;
    }

    private void Inhibit()
    {
        _inhibitedOffset = _offset;
        _inhibited = true;
    }

    private void Uninhibit() => _inhibited = false;

    private double PrimaryLength()
    {
        var usable = Usable();
        return IsHorizontal() ? usable.Width : usable.Height;
    }

    private double SecondaryLength()
    {
        var usable = Usable();
        return IsHorizontal() ? usable.Height : usable.Width;
    }

    private double VisibleLength(Cell cell)
    {
        var view = Viewport();
        var box = cell.Logical;
        var horizontal = IsHorizontal();
        var start = horizontal ? Math.Max(view.X, box.X) : Math.Max(view.Y, box.Y);
        var end = horizontal
            ? Math.Min(view.X + view.Width, box.X + box.Width)
            : Math.Min(view.Y + view.Height, box.Y + box.Height);
        return Math.Max(0, end - start);
    }

    private Rectangle Usable()
    {
        var gaps = _layoutOptions.GapsOut;
        var width = Math.Max(0, _area.Width - gaps.Left - gaps.Right);
        var height = Math.Max(0, _area.Height - gaps.Top - gaps.Bottom);
        return new Rectangle(_area.X + gaps.Left, _area.Y + gaps.Top, width, height);
    }

    private Rectangle Viewport() =>
        _monitor.Width > 0 && _monitor.Height > 0 ? _monitor : _area;

    private bool IsHorizontal() =>
        _scrolling.Direction is ScrollingDirection.Right or ScrollingDirection.Left;

    private bool IsReversed() =>
        _scrolling.Direction is ScrollingDirection.Left or ScrollingDirection.Up;

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
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

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
        public ClientId Id { get; } = id;

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
