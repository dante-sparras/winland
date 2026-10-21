namespace Winland.Core;

/// <summary>
/// Which windows may stay over a fullscreen or maximized Client, as Winland's allowedOverFullscreen flag
/// decides it. Each window holds the flag, true until a fullscreen takes it. The default handlers clear it on every
/// other window of the Workspace when a Client goes fullscreen and set it again when that ends. The scrolling Layout
/// hides the floating windows that were open when a tile went fullscreen, and lets later ones stay over it. A window
/// the flag does not let over a covering Client takes no input, is not visible, and is lowered behind it.
/// </summary>
public sealed partial class WindowManager
{
    private readonly Dictionary<WindowId, bool> _overFullscreen = [];
    private readonly HashSet<WindowId> _hidingSeen = [];
    private readonly HashSet<WindowId> _belowFullscreen = [];
    private readonly Dictionary<WorkspaceId, DefaultFullscreen> _defaultFullscreen = [];
    private readonly Dictionary<WorkspaceId, ScrollHiding> _scrollHiding = [];

    private sealed record DefaultFullscreen(string Floating, string Tiled);

    private sealed class ScrollHiding
    {
        public WindowId? Last { get; set; }

        public FullscreenMode LastMode { get; set; }

        public HashSet<WindowId> Hidden { get; } = [];
    }

    private bool OverFullscreenFlag(WindowId window) => !_overFullscreen.TryGetValue(window, out var allowed) || allowed;

    private void SetOverFullscreen(WindowId window, bool allowed) => _overFullscreen[window] = allowed;

    // Runs after every change, as Winland's fullscreen handlers and the scrolling Layout's recalculation do.
    // The z-order requests that lower a window the flag now keeps under a covering Client, and raise one it lets out.
    private List<ZOrderRequest> SyncFullscreenHiding()
    {
        foreach (var desk in _workspaces)
        {
            SeeNewWindows(desk);
            SyncDefaultHandlers(desk);
            if (desk.Layout is ScrollingLayout)
                SyncScrollingHiding(desk);
        }

        var requests = new List<ZOrderRequest>();
        var blocked = new HashSet<WindowId>();
        foreach (var desk in _workspaces)
        {
            foreach (var client in desk.Layout.Clients)
            {
                if (BlockedByFullscreen(desk, client))
                    blocked.Add(client.Window);
            }
        }

        foreach (var window in blocked)
        {
            if (!_belowFullscreen.Contains(window) && Find(window) is { Floating: true } && IsShownClient(window))
                requests.Add(new ZOrderRequest(window, Top: false));
        }

        foreach (var window in _stacking)
        {
            if (_belowFullscreen.Contains(window) && !blocked.Contains(window) && Find(window) is { Floating: true } && IsShownClient(window))
                requests.Add(new ZOrderRequest(window, Top: true));
        }

        _belowFullscreen.Clear();
        _belowFullscreen.UnionWith(blocked);
        return requests;
    }

    // A floating window is allowed over a fullscreen one when it opens. A tile that opens under a covering Client is not.
    private void SeeNewWindows(LiveWorkspace desk)
    {
        var covering = CoveringWindow(desk);
        foreach (var client in desk.Layout.Clients)
        {
            if (!_hidingSeen.Add(client.Window))
                continue;

            if (client.Floating)
                SetOverFullscreen(client.Window, true);
            else if (covering is Client over && over.Window != client.Window)
                SetOverFullscreen(client.Window, false);
        }
    }

    // The floating and the tiled default handlers each mark every other window when their fullscreen Client changes.
    private void SyncDefaultHandlers(LiveWorkspace desk)
    {
        var floating = DefaultKey(desk, floating: true);
        var tiled = DefaultKey(desk, floating: false);
        _defaultFullscreen.TryGetValue(desk.Id, out var before);
        _defaultFullscreen[desk.Id] = new DefaultFullscreen(floating, tiled);
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
            if (!own && !IsPinnedClient(client))
                SetOverFullscreen(client.Window, !set);
        }
    }

    // Winland's scrolling fullscreen handler, as its recalculation runs it.
    private void SyncScrollingHiding(LiveWorkspace desk)
    {
        if (!_scrollHiding.TryGetValue(desk.Id, out var state))
            _scrollHiding[desk.Id] = state = new ScrollHiding();

        if (state.Last is WindowId gone && desk.ClientOf(gone) is null)
            state.Last = null;

        var held = LayoutCovering(desk);
        if (CoveringWindow(desk) is Client covering && !covering.LayoutFullscreen)
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
        var covering = CoveringWindow(desk);
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
                if (client.Window != held.Window && !IsPinnedClient(client) && client.Floating)
                    state.Hidden.Add(client.Window);
            }

            MarkScrolling(desk, set: true);
            return;
        }

        foreach (var client in desk.Layout.Clients)
        {
            if (client.Window != held.Window && !IsPinnedClient(client))
                SetOverFullscreen(client.Window, client.Floating && !state.Hidden.Contains(client.Window));
        }
    }

    private bool OutOfSync(LiveWorkspace desk, Client held, ScrollHiding state)
    {
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Window == held.Window || IsPinnedClient(client))
                continue;

            if (AllowedOverFullscreen(desk, client) != (client.Floating && !state.Hidden.Contains(client.Window)))
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
            if (!heldCovering && !IsPinnedClient(client))
                SetOverFullscreen(client.Window, !set);
        }
    }

    private bool IsPinnedClient(Client client) => client.Floating && _pinned.Contains(client.Window);

    // The Client covering the Workspace, as Winland picks it: a floating fullscreen one first, then a tile
    // fullscreen the default way, then a tile the Layout holds while it fills the view.
    private static Client? CoveringWindow(LiveWorkspace desk)
    {
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

    private static Client? LayoutCovering(LiveWorkspace desk)
    {
        foreach (var client in desk.Layout.Clients)
        {
            if (client is { LayoutFullscreen: true, Fullscreen: not FullscreenMode.None } && desk.IsCovering(client))
                return client;
        }

        return null;
    }

    private bool AllowedOverFullscreen(LiveWorkspace desk, Client client)
    {
        var covering = CoveringWindow(desk);
        return covering?.Window == client.Window
            || IsPinnedClient(client)
            || OverFullscreenFlag(client.Window)
            || (covering is not null && desk.GroupOf(covering.Window) is WindowGroup group && group.Contains(client.Window));
    }

    private bool BlockedByFullscreen(LiveWorkspace desk, Client client) =>
        CoveringWindow(desk) is not null && !AllowedOverFullscreen(desk, client);
}
