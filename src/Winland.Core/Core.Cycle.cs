namespace Winland;

public sealed partial class Core
{
    /// <summary>
    /// <c>cyclenext [prev] [tiled] [floating]</c> focuses the next window on the focused window's Workspace, as the
    /// pinned release's window cycle does. <c>prev</c> goes the other way. <c>tiled</c> or <c>floating</c> alone
    /// takes only that kind of window, and both or neither take either. With <c>tiled</c> from a tile in master or
    /// monocle, the Layout's own <c>cyclenext</c> or <c>cycleprev</c> runs instead.
    /// </summary>
    private Outcome CycleNext(string arguments, bool allowsElevation)
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
                    return FocusClient(client.Window, name, allowsElevation);
            }

            return View([], dispatcher: name, allowsElevation: allowsElevation);
        }

        if (tiled && desk.ClientOf(current) is { Floating: false } && LayoutFactory.NameOf(desk.Layout) is "master" or "monocle")
            return Execute("layoutmsg", previous ? "cycleprev" : "cyclenext", allowsElevation);

        bool? only = tiled != floating ? floating : null;
        return CycleFrom(desk, current, previous, only) is WindowId next
            ? FocusClient(next, name, allowsElevation)
            : View([], dispatcher: name, allowsElevation: allowsElevation);
    }
}
