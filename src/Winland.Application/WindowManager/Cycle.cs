namespace Winland.Application;

/// <summary>The <c>cyclenext</c> dispatcher.</summary>
public sealed partial class WindowManager
{
    /// <summary>
    /// <c>cyclenext [prev] [tiled] [floating]</c> focuses the next window on the focused window's Workspace, as the
    /// window cycle does. A window selector after a comma names the window, and the cycle runs on
    /// that window's Workspace, then focuses the window it lands on. <c>prev</c> goes the other way. <c>tiled</c> or
    /// <c>floating</c> alone takes only that kind of window, and both or neither take either.
    /// </summary>
    private Outcome CycleNext(string arguments)
    {
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

        bool? only = tiled != floating ? floating : null;
        if (ActiveWindow() is not WindowId current || WorkspaceContaining(current) is not LiveWorkspace desk)
        {
            return CycleFrom(CurrentWorkspace(), null, previous, only) is WindowId first
                ? FocusClient(first)
                : View([]);
        }

        return CycleFrom(desk, current, previous, only) is WindowId next
            ? FocusClient(next)
            : View([]);
    }

}
