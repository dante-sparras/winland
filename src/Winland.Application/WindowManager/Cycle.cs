namespace Winland.Application;

/// <summary>The <c>cyclenext</c> dispatcher.</summary>
public sealed partial class WindowManager
{
    /// <summary>
    /// <c>cyclenext [prev] [tiled] [floating]</c> focuses the next window on the focused window's Workspace, as the
    /// window cycle does. A window selector after a comma names the window, and the cycle runs on
    /// that window's Workspace, then focuses the window it lands on. <c>prev</c> goes the other way. <c>tiled</c> or
    /// <c>floating</c> alone takes only that kind of window, and both or neither take either. With <c>tiled</c> from a
    /// tile in master or monocle, that Workspace's <c>cyclenext</c> or <c>cycleprev</c> runs instead.
    /// </summary>
    private Outcome CycleNext(string arguments)
    {
        const string name = "cyclenext";
        var previous = false;
        var tiled = false;
        var floating = false;
        foreach (var word in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (word.Equals("prev", StringComparison.OrdinalIgnoreCase))
                previous = true;
            else if (word.Equals("next", StringComparison.OrdinalIgnoreCase))
                previous = false;
            else if (word.Equals("tiled", StringComparison.OrdinalIgnoreCase))
                tiled = true;
            else if (word.Equals("floating", StringComparison.OrdinalIgnoreCase))
                floating = true;
        }

        // With nothing focused, the first window on the focused Monitor's Workspace takes focus.
        if (ActiveWindow() is not WindowId current || WorkspaceContaining(current) is not LiveWorkspace desk)
        {
            var shown = FocusedWorkspace();
            foreach (var client in shown.Layout.Clients)
            {
                if (!client.Minimized)
                    return FocusClient(client.Window, name);
            }

            return View([], dispatcher: name);
        }

        if (tiled && desk.ClientOf(current) is { Floating: false } && LayoutFactory.NameOf(desk.Layout) is "master" or "monocle")
        {
            // On the Workspace already on screen this is the layout message, which reads the window the dispatcher names.
            // Another Workspace runs that message on its own Layout, then the window it names takes the focus.
            if (desk.Id == CurrentWorkspace().Id)
                return Execute("layoutmsg", previous ? "cycleprev" : "cyclenext");

            return CycleLayout(desk, current, previous, name);
        }

        bool? only = tiled != floating ? floating : null;
        return CycleFrom(desk, current, previous, only) is WindowId next
            ? FocusClient(next, name)
            : View([], dispatcher: name);
    }

    private Outcome CycleLayout(LiveWorkspace desk, WindowId current, bool previous, string name)
    {
        var covered = desk.Covered();
        var placements = desk.Layout.LayoutMessage(current, previous ? "cycleprev" : "cyclenext");
        if (desk.Layout.LayoutMessageError is string layoutError)
            return View(placements, dispatcher: name) with { Error = layoutError };

        var focus = desk.Layout.LayoutMessageFocus ?? RevealedTile(desk, covered, placements);
        if (focus is null || HasNoFocus(focus.Value))
        {
            return IsShown(desk.Id)
                ? Present(desk, covered, placements, dispatcher: name)
                : View([], dispatcher: name);
        }

        if (!IsShown(desk.Id))
            return FocusClient(focus.Value, name);

        var laid = Present(desk, covered, placements, dispatcher: name);
        return laid.Then(FocusClient(focus.Value, name));
    }

    // Monocle does not name a window to focus. The tile it just uncovered was covered before the cycle.
    private static WindowId? RevealedTile(LiveWorkspace desk, HashSet<WindowId> covered, IReadOnlyList<Placement> placements)
    {
        foreach (var placement in placements)
        {
            foreach (var client in desk.Layout.Clients)
            {
                if (client.Id != placement.Client || client.Floating || client.Minimized || !covered.Contains(client.Window))
                    continue;

                return client.Window;
            }
        }

        return null;
    }
}
