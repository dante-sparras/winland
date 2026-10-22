namespace Winland.Domain;

/// <summary>
/// Which windows may stay over a fullscreen or maximized Client, as the allowedOverFullscreen flag decides it. Each
/// window holds the flag, true until a fullscreen takes it. The default handlers clear it on every other window of
/// the Workspace when a Client goes fullscreen and set it again when that ends. The scrolling Layout hides the
/// floating windows that were open when a tile went fullscreen, and lets later ones stay over it. A window the flag
/// does not let over a covering Client takes no input, is not visible, and is lowered behind it.
/// </summary>
/// <param name="isPinned">True for a pinned Client, which always stays over a fullscreen one.</param>
internal sealed class FullscreenCover(Func<WindowId, bool> isPinned)
{
    private readonly Dictionary<WindowId, bool> _flags = [];
    private readonly HashSet<WindowId> _seen = [];
    private readonly HashSet<WindowId> _below = [];
    private readonly Dictionary<WorkspaceId, DefaultFullscreen> _defaults = [];
    private readonly Dictionary<WorkspaceId, ScrollHiding> _scrolling = [];

    /// <summary>The allowedOverFullscreen flag of <paramref name="window"/>, true until a fullscreen takes it.</summary>
    public bool Flag(WindowId window) => !_flags.TryGetValue(window, out var allowed) || allowed;

    public void SetFlag(WindowId window, bool allowed) => _flags[window] = allowed;

    /// <summary>True for a window the last <see cref="Sync"/> lowered behind a covering Client.</summary>
    public bool IsBelow(WindowId window) => _below.Contains(window);

    /// <summary>A closed window leaves every record.</summary>
    public void Forget(WindowId window)
    {
        _flags.Remove(window);
        _seen.Remove(window);
        _below.Remove(window);
    }

    /// <summary>
    /// Brings every Workspace's flags up to date, as the fullscreen handlers and the scrolling Layout's recalculation do
    /// after every change. Returns the windows to lower behind a covering Client (<c>Top</c> false) and the ones to
    /// raise again (<c>Top</c> true), in the order of <paramref name="stacking"/>. Only a window
    /// <paramref name="restackable"/> accepts is moved.
    /// </summary>
    public IReadOnlyList<(WindowId Window, bool Top)> Sync(
        IEnumerable<LiveWorkspace> workspaces,
        IEnumerable<WindowId> stacking,
        Func<WindowId, bool> restackable)
    {
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(stacking);
        ArgumentNullException.ThrowIfNull(restackable);
        var desks = workspaces.ToList();
        foreach (var desk in desks)
        {
            SeeNewWindows(desk);
            SyncDefaultHandlers(desk);
            if (desk.Layout is ScrollingLayout)
                SyncScrollingHiding(desk);
        }

        var blocked = new HashSet<WindowId>();
        foreach (var desk in desks)
        {
            foreach (var client in desk.Layout.Clients)
            {
                if (Covering(desk) is not null && !AllowedOver(desk, client))
                    blocked.Add(client.Window);
            }
        }

        var requests = new List<(WindowId Window, bool Top)>();
        foreach (var window in blocked)
        {
            if (!_below.Contains(window) && restackable(window))
                requests.Add((window, false));
        }

        foreach (var window in stacking)
        {
            if (_below.Contains(window) && !blocked.Contains(window) && restackable(window))
                requests.Add((window, true));
        }

        _below.Clear();
        _below.UnionWith(blocked);
        return requests;
    }

    /// <summary>
    /// True when <paramref name="client"/> may stay over the Client covering <paramref name="desk"/>: it is that
    /// Client, it is pinned, its flag lets it, or it is in that Client's Group.
    /// </summary>
    public bool AllowedOver(LiveWorkspace desk, Client client)
    {
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(client);
        var covering = Covering(desk);
        return covering?.Window == client.Window
            || isPinned(client.Window)
            || Flag(client.Window)
            || (covering is not null && desk.GroupOf(covering.Window) is WindowGroup group && group.Contains(client.Window));
    }

    /// <summary>
    /// The Client covering <paramref name="desk"/>: a floating fullscreen one first, then a tile fullscreen the default
    /// way, then a tile the Layout holds while it fills the view.
    /// </summary>
    public static Client? Covering(LiveWorkspace desk)
    {
        ArgumentNullException.ThrowIfNull(desk);
        Client? tiled = null;
        Client? held = null;
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Fullscreen == FullscreenMode.None || client.Minimized)
                continue;

            if (!client.LayoutFullscreen)
            {
                if (client.Floating)
                    return client;

                tiled ??= client;
            }
            else if (desk.IsCovering(client))
            {
                held ??= client;
            }
        }

        return tiled ?? held;
    }

    // A floating window is allowed over a fullscreen one when it opens. A tile that opens under a covering Client is not.
    private void SeeNewWindows(LiveWorkspace desk)
    {
        var covering = Covering(desk);
        foreach (var client in desk.Layout.Clients)
        {
            if (!_seen.Add(client.Window))
                continue;

            if (client.Floating)
                SetFlag(client.Window, true);
            else if (covering is Client over && over.Window != client.Window)
                SetFlag(client.Window, false);
        }
    }

    // The floating and the tiled default handlers each mark every other window when their fullscreen Client changes.
    private void SyncDefaultHandlers(LiveWorkspace desk)
    {
        var floating = DefaultKey(desk, floating: true);
        var tiled = DefaultKey(desk, floating: false);
        _defaults.TryGetValue(desk.Id, out var before);
        _defaults[desk.Id] = new DefaultFullscreen(floating, tiled);
        if (before is null)
        {
            if (floating.Length > 0)
                MarkDefault(desk, floatingHandler: true, set: true);
            if (tiled.Length > 0)
                MarkDefault(desk, floatingHandler: false, set: true);
            return;
        }

        if (before.Floating != floating)
            MarkDefault(desk, floatingHandler: true, set: floating.Length > 0);
        if (before.Tiled != tiled)
            MarkDefault(desk, floatingHandler: false, set: tiled.Length > 0);
    }

    private static string DefaultKey(LiveWorkspace desk, bool floating)
    {
        var key = new System.Text.StringBuilder();
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Fullscreen != FullscreenMode.None && !client.LayoutFullscreen && client.Floating == floating)
                key.Append(client.Window.Value).Append(':').Append((int)client.Fullscreen).Append(';');
        }

        return key.ToString();
    }

    private void MarkDefault(LiveWorkspace desk, bool floatingHandler, bool set)
    {
        foreach (var client in desk.Layout.Clients)
        {
            var own = client.Fullscreen != FullscreenMode.None && !client.LayoutFullscreen && client.Floating == floatingHandler;
            if (!own && !isPinned(client.Window))
                SetFlag(client.Window, !set);
        }
    }

    // The scrolling fullscreen handler, as its recalculation runs it.
    private void SyncScrollingHiding(LiveWorkspace desk)
    {
        if (!_scrolling.TryGetValue(desk.Id, out var state))
            _scrolling[desk.Id] = state = new ScrollHiding();

        if (state.Last is WindowId gone && desk.ClientOf(gone) is null)
            state.Last = null;

        var held = LayoutCovering(desk);
        if (Covering(desk) is Client covering && !covering.LayoutFullscreen)
        {
            // A default fullscreen covers the tape. A floating one alone hides every other window.
            if (covering.Floating && held is null)
            {
                state.Hidden.Clear();
                MarkScrolling(desk, set: true);
            }

            return;
        }

        var last = state.Last is WindowId lastWindow ? desk.ClientOf(lastWindow) : null;
        var positive = held is not null && (held.Window != last?.Window || held.Fullscreen != state.LastMode);
        var away = last is not null && last.LayoutFullscreen && last.Fullscreen != FullscreenMode.None
            && (held is null || last.Window != held.Window);
        var negative = away || (held is null && last is not null && last.Fullscreen == FullscreenMode.None);
        if (positive || away || negative || (held is not null && OutOfSync(desk, held, state)))
            HideUnderScrolling(desk, state, held, last);
    }

    private void HideUnderScrolling(LiveWorkspace desk, ScrollHiding state, Client? held, Client? last)
    {
        var covering = Covering(desk);
        if (covering is null)
        {
            state.Last = null;
            state.Hidden.Clear();
            MarkScrolling(desk, set: false);
            return;
        }

        if (held is null || covering.Window != held.Window)
            return;

        if (last?.Window != held.Window || held.Fullscreen != state.LastMode)
        {
            state.Hidden.Clear();
            state.Last = held.Window;
            state.LastMode = held.Fullscreen;
            foreach (var client in desk.Layout.Clients)
            {
                if (client.Window != held.Window && !isPinned(client.Window) && client.Floating)
                    state.Hidden.Add(client.Window);
            }

            MarkScrolling(desk, set: true);
            return;
        }

        foreach (var client in desk.Layout.Clients)
        {
            if (client.Window != held.Window && !isPinned(client.Window))
                SetFlag(client.Window, client.Floating && !state.Hidden.Contains(client.Window));
        }
    }

    private bool OutOfSync(LiveWorkspace desk, Client held, ScrollHiding state)
    {
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Window == held.Window || isPinned(client.Window))
                continue;

            if (AllowedOver(desk, client) != (client.Floating && !state.Hidden.Contains(client.Window)))
                return true;
        }

        return false;
    }

    // Every window but a covering tile the Layout holds, and a pinned one, takes the flag.
    private void MarkScrolling(LiveWorkspace desk, bool set)
    {
        foreach (var client in desk.Layout.Clients)
        {
            var heldCovering = client.LayoutFullscreen && desk.IsCovering(client);
            if (!heldCovering && !isPinned(client.Window))
                SetFlag(client.Window, !set);
        }
    }

    private static Client? LayoutCovering(LiveWorkspace desk)
    {
        foreach (var client in desk.Layout.Clients)
        {
            if (client is { LayoutFullscreen: true, Fullscreen: not FullscreenMode.None } && desk.IsCovering(client))
                return client;
        }

        return null;
    }

    private sealed record DefaultFullscreen(string Floating, string Tiled);

    private sealed class ScrollHiding
    {
        public WindowId? Last { get; set; }

        public FullscreenMode LastMode { get; set; }

        public HashSet<WindowId> Hidden { get; } = [];
    }
}
