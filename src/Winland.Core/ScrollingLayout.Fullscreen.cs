namespace Winland;

// Fullscreen and maximized tiles the scrolling Layout lays out itself, as the pinned release's scrolling Layout does.
// Such a tile has a column of its own, as wide as the Monitor when fullscreen and as the view when maximized, and it
// stays on the tape. It covers the Workspace while its column fills the view, and scrolls away with the tape.
internal sealed partial class ScrollingLayout
{
    // The width each held tile's column had before it went fullscreen, given back when it leaves.
    private readonly Dictionary<ClientId, double> _restoreWidths = [];
    private bool _syncing;

    public IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode) =>
        SetFullscreen(window, mode, clientMode, layoutAware: true);

    public IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode, bool layoutAware)
    {
        var id = new ClientId(window.Value);
        var index = _clients.FindIndex(client => client.Id == id);
        if (index < 0)
            return [];

        var client = _clients[index];
        if (client.Floating || !layoutAware || CellFor(window) is not { Column: Column column } cell)
            return DefaultFullscreen(window, client, mode, clientMode);

        // A Client fullscreen the default way leaves that mode as the Layout takes it.
        var held = client.LayoutFullscreen ? client.Fullscreen : FullscreenMode.None;
        if (held != mode && mode != FullscreenMode.None)
        {
            _restoreWidths[id] = held != FullscreenMode.None && _restoreWidths.TryGetValue(id, out var before) ? before : column.Width;
            if (column.Cells.Count > 1)
            {
                Expel(cell, _columns.IndexOf(column));
                column = cell.Column!;
            }

            column.Width = mode == FullscreenMode.Fullscreen ? FullscreenWidth() : 1;
        }
        else if (held != mode && held == FullscreenMode.Fullscreen)
        {
            // A fullscreen column is wider than the view, so the view moves back by the work area's inset.
            AdjustOffset(Viewport().X - Usable().X);
        }

        _clients[index] = client with
        {
            Fullscreen = mode,
            FullscreenClient = clientMode,
            LayoutFullscreen = mode != FullscreenMode.None || clientMode != FullscreenMode.None,
        };
        if (mode == FullscreenMode.None && clientMode == FullscreenMode.None && _restoreWidths.Remove(id, out var width))
            column.Width = width;

        if (held != mode)
            CenterOrFit(column);

        Recalculate();
        return Visible();
    }

    public bool FullscreenCovers(WindowId window)
    {
        if (CellFor(window) is not { Column: Column column } cell || HeldFullscreen(cell.Id) is null)
            return true;

        return column.Cells.Count == 1 && TryStrip(column, out var start, out var size, out _) && Covers(start, size);
    }

    private IReadOnlyList<Placement> DefaultFullscreen(WindowId window, Client client, FullscreenMode mode, FullscreenMode clientMode)
    {
        var id = client.Id;
        if (client.LayoutFullscreen && _restoreWidths.Remove(id, out var width) && CellFor(window)?.Column is Column column)
            column.Width = width;

        if (!FullscreenClients.Assign(_clients, window, mode, clientMode))
            return [];

        if (client.LayoutFullscreen)
            Recalculate();

        var placements = new List<Placement>(Visible());
        if (mode != FullscreenMode.None && !placements.Exists(placement => placement.Client == id) && TryBounds(window, out var bounds))
            placements.Add(new Placement(id, bounds));

        return placements;
    }

    // The mode the Layout holds this tile in, or null when it holds none.
    private FullscreenMode? HeldFullscreen(ClientId id)
    {
        foreach (var client in _clients)
        {
            if (client.Id == id)
                return client is { LayoutFullscreen: true, Fullscreen: not FullscreenMode.None } ? client.Fullscreen : null;
        }

        return null;
    }

    // A held tile's column covers the view when it starts and ends within a pixel of the view's edges.
    private bool Covers(double start, double size) =>
        start <= _offset + 1 && start + size >= _offset + PrimaryLength() - 1;

    // A fullscreen column is as wide as the Monitor, in shares of the view.
    private double FullscreenWidth()
    {
        var usable = Usable();
        var monitor = Viewport();
        var view = IsHorizontal() ? usable.Width : usable.Height;
        var screen = IsHorizontal() ? monitor.Width : monitor.Height;
        return view <= 0 ? 1 : Math.Max(1, screen / (double)view);
    }

    // A held tile covering the view fills the Monitor, or the work area when maximized. Scrolled away, it keeps that
    // height and its place on the tape. A maximized tile keeps its gaps and border, a fullscreen one has neither.
    private void PlaceFullscreen(Cell cell, FullscreenMode mode, int index, double start, double size, double primaryPos)
    {
        var usable = Usable();
        var cover = mode == FullscreenMode.Fullscreen ? Viewport() : usable;
        var horizontal = IsHorizontal();
        double x = cover.X;
        double y = cover.Y;
        double width = cover.Width;
        double height = cover.Height;
        if (!Covers(start, size))
        {
            if (horizontal)
            {
                x = usable.X + primaryPos;
                width = size;
            }
            else
            {
                y = usable.Y + primaryPos;
                height = size;
            }
        }

        if (mode == FullscreenMode.Maximized)
        {
            var gaps = GapsFor(index, 0, 1, horizontal);
            var border = _layoutOptions.BorderFor(cell.Id);
            x += gaps.Left + border;
            y += gaps.Top + border;
            width = Math.Max(1, width - gaps.Left - gaps.Right - (border * 2.0));
            height = Math.Max(1, height - gaps.Top - gaps.Bottom - (border * 2.0));
        }

        var box = Pixels(x, y, width, height);
        cell.Logical = box;
        cell.Box = box;
    }

    // A held tile that shares its column, after a consume or a move, leaves fullscreen as in the pinned release. One
    // that floats or leaves the tape is no longer the Layout's to hold.
    private void SyncFullscreen()
    {
        if (_syncing)
            return;

        _syncing = true;
        try
        {
            for (var i = 0; i < _clients.Count; i++)
            {
                var client = _clients[i];
                if (!client.LayoutFullscreen)
                    continue;

                if (client.Floating || CellFor(client.Window) is not { Column: Column column })
                {
                    _restoreWidths.Remove(client.Id);
                    _clients[i] = client with { LayoutFullscreen = false };
                }
                else if (client.Fullscreen != FullscreenMode.None && column.Cells.Count != 1)
                {
                    _ = SetFullscreen(client.Window, FullscreenMode.None, FullscreenMode.None, layoutAware: true);
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
