using System.Collections.Frozen;

namespace Winland.Application;

/// <summary>
/// Every dispatcher Winland runs, in one table: what it runs, and whether it takes a window after a comma in place of
/// the active one. <see cref="Dispatchers"/> lists the same names for the config parser, and a test keeps the two
/// equal. To add a dispatcher, add its name there and its entry here.
/// </summary>
public sealed partial class WindowManager
{
    /// <summary>
    /// What a dispatcher runs, given the Window Manager, the name it was called by and its arguments.
    /// <paramref name="TakesWindow"/> when <c>name window,arguments</c> runs it on that window.
    /// </summary>
    private sealed record Dispatcher(
        Func<WindowManager, string, string, Outcome> Run,
        bool TakesWindow = false,
        DispatcherTraits Traits = DispatcherTraits.None);

    /// <summary>What else a dispatcher's outcome does, beyond what it runs.</summary>
    [Flags]
    private enum DispatcherTraits
    {
        None = 0,

        /// <summary>Moves the cursor to the window it focuses, under the <c>cursor</c> options.</summary>
        WarpsToFocused = 1,

        /// <summary>Puts the cursor in the middle of a Monitor it leaves with nothing to focus.</summary>
        WarpsToEmptyMonitor = 2,

        /// <summary>A dwindle resize that moves the tiles at once.</summary>
        InstantResize = 4,
    }

    private static DispatcherTraits TraitsOf(string? name) =>
        name is not null && Table.TryGetValue(name, out var dispatcher) ? dispatcher.Traits : DispatcherTraits.None;

    private static readonly FrozenDictionary<string, Dispatcher> Table = BuildTable();

    /// <summary>The names the table runs.</summary>
    internal static IEnumerable<string> DispatcherNames => Table.Keys;

    private static FrozenDictionary<string, Dispatcher> BuildTable()
    {
        var table = new Dictionary<string, Dispatcher>(StringComparer.OrdinalIgnoreCase)
        {
            // Programs and the daemon
            ["exec"] = new(static (m, n, a) => m.Exec(n, a)),
            ["exit"] = new(static (m, _, _) => m.ExitDaemon()),
            ["sendshortcut"] = new(static (m, _, a) => m.SendShortcut(a)),

            // Workspaces and Monitors
            ["workspace"] = new(static (m, n, a) => m.WorkspaceDispatch(n, a)),
            ["togglespecialworkspace"] = new(static (m, _, a) => m.ToggleSpecial(a)),
            ["movetoworkspace"] = new(static (m, _, a) => m.MoveWindowToWorkspace(a, silent: false)),
            ["movetoworkspacesilent"] = new(static (m, _, a) => m.MoveWindowToWorkspace(a, silent: true)),
            ["focusmonitor"] = new(static (m, _, a) => m.FocusMonitorDispatch(a), Traits: DispatcherTraits.WarpsToEmptyMonitor),
            ["movecurrentworkspacetomonitor"] = new(static (m, _, a) => m.MoveCurrentWorkspace(a)),

            // Focus
            ["movefocus"] = new(static (m, _, a) => m.MoveFocusDirection(a), Traits: DispatcherTraits.WarpsToFocused | DispatcherTraits.WarpsToEmptyMonitor),
            ["focuswindow"] = new(static (m, _, a) => m.FocusWindow(a)),
            ["cyclenext"] = new(static (m, _, a) => m.CycleNext(a), TakesWindow: true, Traits: DispatcherTraits.WarpsToFocused),
            ["focusurgentorlast"] = new(static (m, _, _) => m.FocusUrgentOrLast(), Traits: DispatcherTraits.WarpsToFocused),

            // Windows
            ["killactive"] = new(static (m, n, a) => m.LayoutDispatch(n, a)),
            ["closeall"] = new(static (m, _, _) => m.CloseAll()),
            ["movewindow"] = new(static (m, _, a) => m.MoveWindowDirection(a), TakesWindow: true),
            ["swapwindow"] = new(static (m, _, a) => m.SwapWindow(a), TakesWindow: true),
            ["centerwindow"] = new(static (m, _, _) => m.CenterWindow(), TakesWindow: true),
            ["togglefloating"] = new(static (m, _, a) => m.FloatWindow(a)),
            ["pin"] = new(static (m, _, a) => m.PinWindow(a)),
            ["pseudo"] = new(static (m, _, a) => m.PseudoWindow(a)),
            ["fullscreen"] = new(static (m, n, a) => m.FullscreenDispatcher(n, a), TakesWindow: true),
            ["maximize"] = new(static (m, n, a) => m.FullscreenDispatcher(n, a)),
            ["resizeactive"] = new(static (m, n, a) => m.LayoutDispatch(n, a), Traits: DispatcherTraits.InstantResize),
            ["setprop"] = new(static (m, _, a) => m.SetPropDispatch(a)),

            // Layouts
            ["layoutmsg"] = new(static (m, n, a) => m.LayoutDispatch(n, a)),
            ["layout"] = new(static (m, n, a) => m.LayoutDispatch(n, a)),
            ["cyclelayout"] = new(static (m, _, a) => m.CycleWorkspaceLayout(a)),

            // Groups
            ["togglegroup"] = new(static (m, n, _) => m.ToggleGroup(n), TakesWindow: true),
            ["changegroupactive"] = new(static (m, n, a) => m.ChangeGroupActive(a.Trim(), n), TakesWindow: true),
            ["movegroupwindow"] = new(static (m, n, a) => m.MoveGroupWindow(a.Trim(), n)),
            ["moveintogroup"] = new(static (m, n, a) => m.MoveIntoGroup(a.Trim(), n), TakesWindow: true),
            ["moveoutofgroup"] = new(static (m, n, a) => m.MoveOutOfGroup(a.Trim(), n)),

            // Accepted with no effect. A mouse bind's resizewindow runs in the drag code, not here.
            ["resizewindow"] = new(static (m, n, _) => m.View([], dispatcher: n)),
        };

        // A dwindle layout message is also a dispatcher of its own name.
        foreach (var message in DwindleMessages.Names)
            table[message] = new(static (m, n, a) => m.LayoutDispatch(n, a));

        return table.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
