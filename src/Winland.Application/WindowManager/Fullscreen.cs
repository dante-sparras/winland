using static Winland.Domain.OrdinalText;

namespace Winland.Application;

/// <summary>The <c>fullscreen</c> and <c>maximize</c> dispatchers, and what a fullscreen Client covers.</summary>
public sealed partial class WindowManager
{
    private Outcome FullscreenDispatcher(string name, string arguments)
    {
        var text = Is(name, "maximize") ? Join("1", arguments) : arguments;
        var parsed = FullscreenCommands.Parse(text);
        if (parsed.Ignored)
            return View([]);

        if (ActiveWindow() is not WindowId window || WorkspaceContaining(window) is not LiveWorkspace desk || desk.ClientOf(window) is not Client client)
            return MissingWindow();

        if (!FullscreenCommands.Changed(client.Fullscreen, parsed.Mode, parsed.Action, out var next))
            return View([]);

        return CommitFullscreen(window, next, parsed.LayoutAware);
    }

    private Outcome CommitFullscreen(WindowId window, FullscreenMode mode, bool layoutAware = true)
    {
        var desk = WorkspaceContaining(window);
        if (desk is null || desk.ClientOf(window) is not Client client)
            return MissingWindow();

        var covered = desk.Covered();
        if (SetFullscreen(desk, window, mode, layoutAware) is not { } set)
            return View([]);

        var placements = Restamp(desk, set);
        if (!_workspaces.Shown.IsShown(desk.Id))
            return View([]);

        return Present(desk, covered, placements);
    }

    // Every change of a Client's fullscreen or maximized mode but the end of one goes through here. A Pinned Client
    // stays as it is, and the request changes nothing: null.
    private IReadOnlyList<Placement>? SetFullscreen(LiveWorkspace desk, WindowId window, FullscreenMode mode, bool layoutAware = true)
    {
        if (desk.ClientOf(window) is null || _pins.IsPinned(window))
            return null;

        return desk.Layout.SetFullscreen(window, mode, layoutAware);
    }

    // Ends a Client's fullscreen or maximized mode.
    private static IReadOnlyList<Placement> EndFullscreen(LiveWorkspace desk, WindowId window) =>
        desk.Layout.SetFullscreen(window, FullscreenMode.None);

    // Reads the Workspace's options again. The Layout's placements when the options changed, with the given ones for the
    // Clients those leave out, such as a floating one, or the ones given.
    private IReadOnlyList<Placement> Restamp(LiveWorkspace desk, IReadOnlyList<Placement> placements)
    {
        var options = desk.Options;
        Stamp(desk);
        if (desk.Options == options)
            return placements;

        var applied = desk.Layout.Apply(desk.Options);
        var merged = new List<Placement>(applied);
        foreach (var placement in placements)
        {
            if (!merged.Exists(other => other.Client == placement.Client))
                merged.Add(placement);
        }

        return merged;
    }

    // A tiled window that opens on a Workspace with a fullscreen Client ends that mode.
    private static IReadOnlyList<Placement> OpenUnderFullscreen(LiveWorkspace desk, WindowId window, IReadOnlyList<Placement> placements)
    {
        if (desk.ClientOf(window) is not { Floating: false } || CoveringOther(desk, window) is not Client covering)
            return placements;

        return EndFullscreen(desk, covering.Window);
    }

    // A request to focus a tiled Client under another fullscreen one ends that mode.
    // A floating Client is focused over it. The Client to focus, and the Layout's placements when a mode changed.
    // A Client its Layout holds fullscreen is left alone, and the Layout scrolls to the Client asked for.
    private WindowId FocusUnderFullscreen(LiveWorkspace desk, WindowId window, out IReadOnlyList<Placement>? placements)
    {
        placements = null;

        // A floating window focused over a default fullscreen one is let over it.
        if (desk.ClientOf(window) is { Floating: true } && CoveringOther(desk, window) is { LayoutFullscreen: false })
            _cover.SetFlag(window, true);

        if (desk.ClientOf(window) is not { Floating: false } || CoveringOther(desk, window) is not { LayoutFullscreen: false } covering)
            return window;

        placements = EndFullscreen(desk, covering.Window);
        return window;
    }

    private static Client? CoveringOther(LiveWorkspace desk, WindowId window)
    {
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Window != window && desk.IsCovering(client))
                return client;
        }

        return null;
    }

    private Outcome MissingWindow() =>
        View([]) with { Error = "error: window not found" };

    private Rectangle CoveringBounds(LiveWorkspace desk)
    {
        var work = WorkArea(desk, _area);
        var screen = MonitorById(desk.Monitor);
        if (screen is not { Arranged: { Enabled: true } monitor })
            return FullscreenGeometry.Cover(FullscreenMode.Fullscreen, work, default);

        var bounds = new Rectangle(monitor.X, monitor.Y, monitor.Width, monitor.Height);
        return bounds;
    }

    private static IReadOnlyList<Placement> Cover(IReadOnlyList<Placement> placements, State snapshot)
    {
        if (placements.Count == 0)
            return placements;

        Dictionary<ClientId, Rectangle>? covering = null;
        foreach (var client in snapshot.Clients)
        {
            if (client.Fullscreen == FullscreenMode.None)
                continue;

            covering ??= new Dictionary<ClientId, Rectangle>();
            covering[client.Id] = client.Bounds;
        }

        if (covering is null)
            return placements;

        var rewritten = new Placement[placements.Count];
        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            rewritten[i] = covering.TryGetValue(placement.Client, out var bounds)
                ? new Placement(placement.Client, bounds)
                : placement;
        }

        return rewritten;
    }

    private static Client? CoveringClient(LiveWorkspace desk, IReadOnlyCollection<WindowId> showing)
    {
        foreach (var client in desk.Layout.Clients)
        {
            if (desk.IsCovering(client) && !client.Minimized && showing.Contains(client.Window))
                return client;
        }

        return null;
    }

    private Rectangle PlacedBounds(Client client, LiveWorkspace desk, Rectangle slot)
    {
        // A Client its Layout holds fullscreen is where the Layout put it.
        if (client.Fullscreen == FullscreenMode.None || client.LayoutFullscreen)
            return slot;

        var work = WorkArea(desk, _area);
        if (client.Fullscreen == FullscreenMode.Maximized)
            return Maximized(client, desk, work);

        var monitor = CoveringBounds(desk);
        if (monitor.Width <= 0 || monitor.Height <= 0)
            monitor = work;

        return FullscreenGeometry.Cover(client.Fullscreen, work, monitor);
    }

    // A maximized Client fills its Workspace's work area as Winland lays it out: less gaps_out for a tiled Client,
    // and less its border.
    private static Rectangle Maximized(Client client, LiveWorkspace desk, Rectangle work)
    {
        var options = desk.Options;
        var area = client.Floating ? work : options.GapsOut.Apply(work);
        var border = options.BorderSize;
        return new Rectangle(
            area.X + border,
            area.Y + border,
            Math.Max(1, area.Width - (2 * border)),
            Math.Max(1, area.Height - (2 * border)));
    }
}
