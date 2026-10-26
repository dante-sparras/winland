namespace Winland.Domain;

/// <summary>
/// Places every Client as floating. A Client that arrived tiled floats only while this Layout holds it,
/// and is tiled again by the Layout that takes it. <c>general:layout</c> does not select this Layout.
/// </summary>
internal sealed class FloatingLayout : Layout, IDragOwner
{
    private readonly DwindleLayout _inner = new();
    private readonly HashSet<ClientId> _wasTiled = [];
    private string? _messageError;

    public IReadOnlyList<Client> Clients => _inner.Clients;

    public string? LayoutMessageError => _messageError;

    MouseDrag IDragOwner.Drag => ((IDragOwner)_inner).Drag;

    public IReadOnlyList<Placement> Apply(LayoutOptions options) => _inner.Apply(options);

    public void Fit(Rectangle area) => _inner.Fit(area);

    public void Readdress(WorkspaceId workspace) => _inner.Readdress(workspace);

    public void Retitle(WindowId window, string title) => _inner.Retitle(window, title);

    public void Reclass(WindowId window, string windowClass) => _inner.Reclass(window, windowClass);

    public void NudgeFloating(int dx, int dy) => _inner.NudgeFloating(dx, dy);

    public void NoteFocus(WindowId window) => _inner.NoteFocus(window);

    public void RankFocus(Func<WindowId, int> recency) => _inner.RankFocus(recency);

    public IReadOnlyList<Placement> Add(
        WindowId window,
        Rectangle area,
        Point? pointer,
        bool floating,
        Rectangle bounds,
        WorkspaceId workspace,
        string windowClass = "",
        string title = "",
        Rectangle monitor = default,
        long processId = 0,
        string initialClass = "",
        string initialTitle = "",
        Size? minSize = null,
        Size? maxSize = null,
        TiledLimits? tiledLimits = null)
    {
        var placed = _inner.Add(
            window,
            area,
            pointer,
            floating: true,
            bounds,
            workspace,
            windowClass,
            title,
            monitor,
            processId,
            initialClass,
            initialTitle,
            minSize,
            maxSize,
            tiledLimits);
        if (!floating && placed.Count > 0)
            _wasTiled.Add(new ClientId(window.Value));

        return placed;
    }

    public IReadOnlyList<Placement> UpdateLimits(WindowId window, Size? minSize, Size? maxSize, TiledLimits? tiledLimits = null) =>
        _inner.UpdateLimits(window, minSize, maxSize, tiledLimits);

    public IReadOnlyList<Placement> Remove(WindowId window)
    {
        _wasTiled.Remove(new ClientId(window.Value));
        return _inner.Remove(window);
    }

    public IReadOnlyList<Placement> Minimize(WindowId window) => _inner.Minimize(window);

    public IReadOnlyList<Placement> Restore(WindowId window) => _inner.Restore(window);

    public bool TryTake(WindowId window, out Taken taken)
    {
        if (!_inner.TryTake(window, out taken))
            return false;

        if (_wasTiled.Remove(new ClientId(window.Value)))
            taken = taken with { Floating = false };

        return true;
    }

    public bool Substitute(WindowId current, WindowId next, Taken incoming, out Taken outgoing)
    {
        if (!_inner.Substitute(current, next, incoming, out outgoing))
            return false;

        var from = new ClientId(current.Value);
        if (_wasTiled.Remove(from))
            _wasTiled.Add(new ClientId(next.Value));

        return true;
    }

    public bool TryBounds(WindowId window, out Rectangle bounds) => _inner.TryBounds(window, out bounds);

    public IReadOnlyList<Placement> Visible() => _inner.Visible();

    public IReadOnlyList<Placement> Placed() => _inner.Placed();

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

    public WindowId? MoveFocus(WindowId active, string arguments) => _inner.MoveFocus(active, arguments);

    public IReadOnlyList<Placement> MoveWindow(WindowId window, string arguments) => _inner.MoveWindow(window, arguments);

    public IReadOnlyList<Placement> ResizeActive(WindowId window, string arguments) => _inner.ResizeActive(window, arguments);

    // There are no tiles to sink a Client into.
    public IReadOnlyList<Placement> ToggleFloating(WindowId window, Point? pointer = null)
    {
        _ = window;
        return [];
    }

    public IReadOnlyList<Placement> Swap(WindowId active, WindowId other) => _inner.Swap(active, other);

    public IReadOnlyList<Placement> LayoutMessage(WindowId? active, string arguments)
    {
        _ = active;
        _ = arguments;
        _messageError = "error: bad layoutmsg";
        return [];
    }

    public IReadOnlyList<Placement> Drag(WindowId window, PointerDrag drag) => _inner.Drag(window, drag);

    public IReadOnlyList<Placement> SetFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode) =>
        _inner.SetFullscreen(window, mode, clientMode);

    public IReadOnlyList<Placement> PlaceFloating(WindowId window, Rectangle bounds) => _inner.PlaceFloating(window, bounds);
}
