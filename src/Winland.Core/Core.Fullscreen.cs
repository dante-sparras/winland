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
        if (ActiveWindow() is not WindowId window || WorkspaceContaining(window) is not LiveWorkspace desk || desk.ClientOf(window) is not Client client)
            return MissingWindow(name, allowsElevation);

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
        var placements = desk.Layout.SetFullscreen(window, mode, clientMode);
        if (!IsShown(desk.Id))
            return View([], dispatcher: dispatcher, allowsElevation: allowsElevation);

        return Present(desk, covered, placements, dispatcher: dispatcher, allowsElevation: allowsElevation);
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

    private Rectangle PlacedBounds(Client client, LiveWorkspace desk, Rectangle slot)
    {
        if (client.Fullscreen == FullscreenMode.None)
            return slot;

        var work = WorkArea(desk, _area);
        var monitor = client.Fullscreen == FullscreenMode.Fullscreen ? CoveringBounds(desk) : default;
        if (client.Fullscreen == FullscreenMode.Fullscreen && (monitor.Width <= 0 || monitor.Height <= 0))
            monitor = work;

        return FullscreenGeometry.Cover(client.Fullscreen, work, monitor);
    }
}
