using static Winland.OrdinalText;

namespace Winland;

public sealed partial class Core
{
    private Outcome FullscreenDispatcher(string name, string arguments, bool allowsElevation)
    {
        if (Is(name, "fullscreenstate"))
            return FullscreenState(arguments, allowsElevation);

        var text = Is(name, "maximize") ? Join("1", arguments) : arguments;
        var parsed = FullscreenCommands.Parse(text);
        if (parsed.Ignored)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (ActiveWindow() is not WindowId window || WorkspaceContaining(window) is not LiveWorkspace desk || desk.ClientOf(window) is not Client client)
            return MissingWindow(name, allowsElevation);

        if (!FullscreenCommands.Changed(client.Fullscreen, parsed.Mode, parsed.Action, out var next))
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        return CommitFullscreen(window, next, next, name, allowsElevation);
    }

    private Outcome FullscreenState(string arguments, bool allowsElevation)
    {
        const string name = "fullscreenstate";
        var selector = FullscreenCommands.StateSelector(arguments);
        Client client;
        if (selector.Length > 0)
        {
            if (!TrySelect(selector, out client, out _))
                return MissingWindow(name, allowsElevation);
        }
        else if (ActiveWindow() is not WindowId active || WorkspaceContaining(active) is not LiveWorkspace desk || desk.ClientOf(active) is not Client focused)
        {
            return MissingWindow(name, allowsElevation);
        }
        else
        {
            client = focused;
        }

        var window = client.Window;

        var target = FullscreenCommands.ResolveState(arguments, client.Fullscreen, client.FullscreenClient);
        if (target.Internal == client.Fullscreen && target.Client == client.FullscreenClient)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        return CommitFullscreen(window, target.Internal, target.Client, name, allowsElevation);
    }

    private Outcome CommitFullscreen(WindowId window, FullscreenMode mode, FullscreenMode clientMode, string? dispatcher, bool allowsElevation)
    {
        var desk = WorkspaceContaining(window);
        if (desk is null)
            return MissingWindow(dispatcher, allowsElevation);

        var covered = desk.Covered();
        var placements = Restamp(desk, desk.Layout.SetFullscreen(window, mode, clientMode));
        if (!IsShown(desk.Id))
            return View([], dispatcher: dispatcher, allowsElevation: allowsElevation);

        return Present(desk, covered, placements, dispatcher: dispatcher, allowsElevation: allowsElevation);
    }

    // An f[] workspace rule follows the fullscreen state, so a Client that covers, uncovers, closes or moves
    // can bring other options, as the pinned release reads its rules again. The Layout's placements when
    // the options changed, or the ones given.
    private IReadOnlyList<Placement> Restamp(LiveWorkspace desk, IReadOnlyList<Placement> placements)
    {
        var options = desk.Options;
        Stamp(desk);
        return desk.Options != options ? desk.Layout.Apply(desk.Options) : placements;
    }

    // misc:on_focus_under_fullscreen for a tiled window that opens on a Workspace with a fullscreen Client, as in the
    // pinned release: 0 opens it without focus, 1 gives it the fullscreen Client's mode, and 2 ends that mode.
    private IReadOnlyList<Placement> OpenUnderFullscreen(LiveWorkspace desk, WindowId window, IReadOnlyList<Placement> placements)
    {
        if (desk.ClientOf(window) is not { Floating: false } || CoveringOther(desk, window) is not Client covering)
            return placements;

        switch (_session.OnFocusUnderFullscreen)
        {
            case 0:
                _awaitingMapFocus.Add(window);
                return placements;
            case 1:
                return desk.Layout.SetFullscreen(window, covering.Fullscreen, covering.Fullscreen);
            default:
                return desk.Layout.SetFullscreen(covering.Window, FullscreenMode.None, FullscreenMode.None);
        }
    }

    // misc:on_focus_under_fullscreen for a request to focus a tiled Client under another fullscreen one, as in the
    // pinned release: 0 focuses the fullscreen Client instead, 1 moves its mode to the Client asked for, and 2 ends it.
    // A floating Client is focused over it. The Client to focus, and the Layout's placements when a mode changed.
    private WindowId FocusUnderFullscreen(LiveWorkspace desk, WindowId window, out IReadOnlyList<Placement>? placements)
    {
        placements = null;
        if (desk.ClientOf(window) is not { Floating: false } || CoveringOther(desk, window) is not Client covering)
            return window;

        switch (_session.OnFocusUnderFullscreen)
        {
            case 0:
                return covering.Window;
            case 1:
                placements = desk.Layout.SetFullscreen(window, covering.Fullscreen, covering.Fullscreen);
                return window;
            default:
                placements = desk.Layout.SetFullscreen(covering.Window, FullscreenMode.None, FullscreenMode.None);
                return window;
        }
    }

    // misc:exit_window_retains_fullscreen, as in the pinned release: the Client that takes the focus from a closed
    // fullscreen or maximized one takes its mode. 1 always, 2 only for the next member of its Group, 3 only otherwise.
    private IReadOnlyList<Placement>? RetainFullscreen(LiveWorkspace desk, WindowId next, FullscreenMode mode, bool grouped)
    {
        var retain = _session.ExitWindowRetainsFullscreen switch
        {
            1 => true,
            2 => grouped,
            3 => !grouped,
            _ => false,
        };
        if (!retain || mode == FullscreenMode.None || desk.ClientOf(next) is null)
            return null;

        return desk.Layout.SetFullscreen(next, mode, mode);
    }

    private static Client? CoveringOther(LiveWorkspace desk, WindowId window)
    {
        foreach (var client in desk.Layout.Clients)
        {
            if (client.Window != window && client.Fullscreen != FullscreenMode.None)
                return client;
        }

        return null;
    }

    private Outcome MissingWindow(string? dispatcher, bool allowsElevation) =>
        View([], dispatcher: dispatcher, allowsElevation: allowsElevation) with { Error = "error: window not found" };

    private Rectangle CoveringBounds(LiveWorkspace desk)
    {
        var work = WorkArea(desk, _area);
        var screen = MonitorById(desk.Monitor);
        if (screen is not { Effective: { Enabled: true } monitor })
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
            if (client.Fullscreen != FullscreenMode.None && !client.Minimized && showing.Contains(client.Window))
                return client;
        }

        return null;
    }

    private Rectangle PlacedBounds(Client client, LiveWorkspace desk, Rectangle slot)
    {
        if (client.Fullscreen == FullscreenMode.None)
            return slot;

        var work = WorkArea(desk, _area);
        if (client.Fullscreen == FullscreenMode.Maximized)
            return Maximized(client, desk, work);

        var monitor = CoveringBounds(desk);
        if (monitor.Width <= 0 || monitor.Height <= 0)
            monitor = work;

        return FullscreenGeometry.Cover(client.Fullscreen, work, monitor);
    }

    // A maximized Client fills its Workspace's work area as the pinned release lays it out: less gaps_out, or
    // general:float_gaps for a floating Client, and less its border.
    private static Rectangle Maximized(Client client, LiveWorkspace desk, Rectangle work)
    {
        var options = desk.Options;
        var area = client.Floating ? options.FloatingWorkArea(work) : options.GapsOut.Apply(work);
        var border = options.BorderSize;
        return new Rectangle(
            area.X + border,
            area.Y + border,
            Math.Max(1, area.Width - (2 * border)),
            Math.Max(1, area.Height - (2 * border)));
    }
}
