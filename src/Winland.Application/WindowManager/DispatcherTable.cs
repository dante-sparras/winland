using System.Collections.Frozen;

namespace Winland.Application;

/// <summary>
/// Every dispatcher Winland runs, in one table: what it runs, whether it takes a window after a comma in place of the
/// active one, and its <see cref="DispatcherTraits"/>. The table is also the list of names the config parser accepts.
/// To add a dispatcher, add its entry here.
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

    private static readonly FrozenDictionary<string, Dispatcher> Table = BuildTable();

    /// <summary>The names the table runs, which the config parser accepts for a bind.</summary>
    public static IReadOnlyCollection<string> DispatcherNames => Table.Keys;

    private static FrozenDictionary<string, Dispatcher> BuildTable()
    {
        var table = new Dictionary<string, Dispatcher>(StringComparer.OrdinalIgnoreCase)
        {
            // Programs and the daemon
            ["exec"] = new(static (m, _, a) => m.Exec(a)),
            ["exit"] = new(static (m, _, _) => m.ExitDaemon()),
            ["sendshortcut"] = new(static (m, _, a) => m.SendShortcut(a)),

            // Workspaces and Monitors
            ["workspace"] = new(static (m, _, a) => m.WorkspaceDispatch(a)),
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
            ["resizeactive"] = new(static (m, n, a) => m.LayoutDispatch(n, a)),
            ["setprop"] = new(static (m, _, a) => m.SetPropDispatch(a)),

            // Layouts
            ["layoutmsg"] = new(static (m, n, a) => m.LayoutDispatch(n, a)),
            ["layout"] = new(static (m, n, a) => m.LayoutDispatch(n, a)),
            ["cyclelayout"] = new(static (m, _, a) => m.CycleWorkspaceLayout(a)),

            // Groups
            ["togglegroup"] = new(static (m, _, _) => m.ToggleGroup(), TakesWindow: true),
            ["changegroupactive"] = new(static (m, _, a) => m.ChangeGroupActive(a.Trim()), TakesWindow: true),
            ["movegroupwindow"] = new(static (m, _, a) => m.MoveGroupWindow(a.Trim())),
            ["moveintogroup"] = new(static (m, _, a) => m.MoveIntoGroup(a.Trim()), TakesWindow: true),
            ["moveoutofgroup"] = new(static (m, _, a) => m.MoveOutOfGroup(a.Trim())),

            // Accepted with no effect. A mouse bind's resizewindow runs in the drag code, not here.
            ["resizewindow"] = new(static (m, _, _) => m.View([])),
        };

        // A dwindle layout message is also a dispatcher of its own name.
        foreach (var message in DwindleMessages.Names)
            table[message] = new(static (m, n, a) => m.LayoutDispatch(n, a));

        return table.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
