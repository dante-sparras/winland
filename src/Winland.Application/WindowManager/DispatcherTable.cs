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
    private sealed record Dispatcher(Func<WindowManager, string, string, Outcome> Run, bool TakesWindow = false);

    private static readonly FrozenDictionary<string, Dispatcher> Table = BuildTable();

    /// <summary>The names the table runs.</summary>
    internal static IEnumerable<string> DispatcherNames => Table.Keys;

    private static FrozenDictionary<string, Dispatcher> BuildTable()
    {
        var table = new Dictionary<string, Dispatcher>(StringComparer.OrdinalIgnoreCase)
        {
            // Programs and the daemon
            ["exec"] = new(static (m, n, a) => m.Exec(n, a)),
            ["execr"] = new(static (m, n, a) => m.Exec(n, a)),
            ["exit"] = new(static (m, _, _) => m.ExitDaemon()),
            ["event"] = new(static (m, _, a) => m.EmitCustom(a)),
            ["dpms"] = new(static (m, _, a) => m.Dpms(a)),
            ["submap"] = new(static (m, n, a) => m.SubmapDispatch(n, a)),
            ["sendshortcut"] = new(static (m, _, a) => m.SendShortcut(a)),

            // Workspaces and Monitors
            ["workspace"] = new(static (m, n, a) => m.WorkspaceDispatch(n, a)),
            ["togglespecialworkspace"] = new(static (m, _, a) => m.ToggleSpecial(a)),
            ["movetoworkspace"] = new(static (m, _, a) => m.MoveWindowToWorkspace(a, silent: false)),
            ["movetoworkspacesilent"] = new(static (m, _, a) => m.MoveWindowToWorkspace(a, silent: true)),
            ["focusmonitor"] = new(static (m, _, a) => m.FocusMonitorDispatch(a)),
            ["movecurrentworkspacetomonitor"] = new(static (m, _, a) => m.MoveCurrentWorkspace(a)),
            ["moveworkspacetomonitor"] = new(static (m, _, a) => m.MoveWorkspace(a)),
            ["swapactiveworkspaces"] = new(static (m, _, a) => m.SwapActiveWorkspaces(a)),
            ["changeworkspaceid"] = new(static (m, _, a) => m.ChangeWorkspaceId(a)),

            // Focus
            ["movefocus"] = new(static (m, _, a) => m.MoveFocusDirection(a)),
            ["focuswindow"] = new(static (m, _, a) => m.FocusWindow(a)),
            ["cyclenext"] = new(static (m, _, a) => m.CycleNext(a), TakesWindow: true),
            ["focuscurrentorlast"] = new(static (m, _, _) => m.FocusCurrentOrLast()),
            ["focusurgentorlast"] = new(static (m, _, _) => m.FocusUrgentOrLast()),

            // Windows
            ["killactive"] = new(static (m, n, a) => m.LayoutDispatch(n, a)),
            ["closewindow"] = new(static (m, _, a) => m.CloseWindow(a)),
            ["closeall"] = new(static (m, _, _) => m.CloseAll()),
            ["killwindow"] = new(static (m, _, a) => m.KillWindow(a)),
            ["movewindow"] = new(static (m, _, a) => m.MoveWindowDirection(a), TakesWindow: true),
            ["swapwindow"] = new(static (m, _, a) => m.SwapWindow(a), TakesWindow: true),
            ["centerwindow"] = new(static (m, _, _) => m.CenterWindow(), TakesWindow: true),
            ["togglefloating"] = new(static (m, _, a) => m.FloatWindow(a, floating: null)),
            ["setfloating"] = new(static (m, _, a) => m.FloatWindow(a, floating: true)),
            ["settiled"] = new(static (m, _, a) => m.FloatWindow(a, floating: false)),
            ["pin"] = new(static (m, _, a) => m.PinWindow(a)),
            ["pseudo"] = new(static (m, _, a) => m.PseudoWindow(a)),
            ["fullscreen"] = new(static (m, n, a) => m.FullscreenDispatcher(n, a), TakesWindow: true),
            ["fullscreenstate"] = new(static (m, n, a) => m.FullscreenDispatcher(n, a), TakesWindow: true),
            ["maximize"] = new(static (m, n, a) => m.FullscreenDispatcher(n, a)),
            ["movewindowpixel"] = new(static (m, _, a) => m.MoveWindowPixel(a)),
            ["resizewindowpixel"] = new(static (m, _, a) => m.ResizeWindowPixel(a)),
            ["resizeactive"] = new(static (m, n, a) => m.LayoutDispatch(n, a)),
            ["alterzorder"] = new(static (m, _, a) => m.AlterZOrder(a, bringToTop: false)),
            ["bringactivetotop"] = new(static (m, _, a) => m.AlterZOrder(a, bringToTop: true)),
            ["setprop"] = new(static (m, _, a) => m.SetPropDispatch(a)),
            ["tagwindow"] = new(static (m, _, a) => m.TagWindow(a)),
            ["cleartags"] = new(static (m, _, a) => m.ClearTags(a)),

            // The cursor
            ["movecursor"] = new(static (m, _, a) => m.MoveCursor(a)),
            ["movecursortocorner"] = new(static (m, _, a) => m.MoveCursorToCorner(a), TakesWindow: true),

            // Layouts
            ["layoutmsg"] = new(static (m, n, a) => m.LayoutDispatch(n, a)),
            ["layout"] = new(static (m, n, a) => m.LayoutDispatch(n, a)),
            ["cyclelayout"] = new(static (m, _, a) => m.CycleWorkspaceLayout(a)),
            ["movetoroot"] = new(static (m, _, a) => m.MoveRoot(a)),

            // Groups
            ["togglegroup"] = new(static (m, n, _) => m.ToggleGroup(n), TakesWindow: true),
            ["changegroupactive"] = new(static (m, n, a) => m.ChangeGroupActive(a.Trim(), n), TakesWindow: true),
            ["movegroupwindow"] = new(static (m, n, a) => m.MoveGroupWindow(a.Trim(), n)),
            ["lockgroups"] = new(static (m, n, a) => m.LockGroups(a.Trim(), n)),
            ["lockactivegroup"] = new(static (m, n, a) => m.LockActiveGroup(a.Trim(), n), TakesWindow: true),
            ["denywindowfromgroup"] = new(static (m, n, a) => m.DenyWindowFromGroup(a.Trim(), n)),
            ["moveintogroup"] = new(static (m, n, a) => m.MoveIntoGroup(a.Trim(), create: false, n), TakesWindow: true),
            ["moveintoorcreategroup"] = new(static (m, n, a) => m.MoveIntoGroup(a.Trim(), create: true, n), TakesWindow: true),
            ["moveoutofgroup"] = new(static (m, n, a) => m.MoveOutOfGroup(a.Trim(), n)),
            ["movewindoworgroup"] = new(static (m, n, a) => m.MoveWindowOrGroup(a.Trim(), n), TakesWindow: true),

            // Accepted with no effect. A mouse bind's resizewindow runs in the drag code, not here.
            ["setignoregrouplock"] = new(static (m, n, _) => m.View([], dispatcher: n)),
            ["resizewindow"] = new(static (m, n, _) => m.View([], dispatcher: n)),
            ["no_op"] = new(static (m, n, _) => m.View([], dispatcher: n)),
            ["forcerendererreload"] = new(static (m, n, _) => m.View([], dispatcher: n)),
        };

        // A dwindle layout message is also a dispatcher of its own name.
        foreach (var message in DwindleMessages.Names)
            table[message] = new(static (m, n, a) => m.LayoutDispatch(n, a));

        return table.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
