using System.Globalization;

namespace Winland.Application;

public static partial class CommandProtocol
{
    public static IReadOnlyList<string> FormatStream(IReadOnlyList<StreamEvent> events, State state)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(state);
        if (events.Count == 0)
            return [];

        var indexed = Index(state);
        var lines = new List<string>(events.Count);
        foreach (var streamEvent in events)
            Append(lines, streamEvent, state, indexed);

        return lines;
    }

    private static void Append(List<string> lines, StreamEvent streamEvent, State state, Indexed indexed)
    {
        switch (streamEvent)
        {
            case ClientOpened opened:
                lines.Add($"openwindow>>{EventAddress(opened.Window)},{WorkspaceNameText(opened.Workspace, indexed)},{EventMonitor(state, opened.Workspace)},{Clean(opened.Class)},{Clean(opened.Title)}");
                break;
            case ClientClosed closed:
                lines.Add($"closewindow>>{EventAddress(closed.Window)}");
                break;
            case FocusChanged focused:
                AppendFocus(lines, focused, state);
                break;
            case ClientRetitled retitled:
                lines.Add("windowtitle>>" + EventAddress(retitled.Window));
                lines.Add($"windowtitlev2>>{EventAddress(retitled.Window)},{Clean(retitled.Title)}");
                // A new title of the focused window is told again as the active window.
                if (retitled.Active)
                    AppendFocus(lines, new FocusChanged(retitled.Window), state);
                break;
            case ClientReclassed reclassed:
                // Winland has no windowclass event. A new class of the focused window is told again as
                // the active window.
                if (reclassed.Active)
                    AppendFocus(lines, new FocusChanged(reclassed.Window), state);
                break;
            case MonitorLayoutChanged:
                // Winland tells monitor.layout_changed to the config only.
                break;
            case ClientKilled killed:
                lines.Add("kill>>" + EventAddress(killed.Window));
                break;
            case ClientUrgent urgent:
                lines.Add("urgent>>" + EventAddress(urgent.Window));
                break;
            case ClientMoved moved:
                lines.Add($"movewindow>>{EventAddress(moved.Window)},{WorkspaceNameText(moved.Workspace, indexed)},{EventMonitor(state, moved.Workspace)}");
                lines.Add($"movewindowv2>>{EventAddress(moved.Window)},{WorkspaceIdText(moved.Workspace, indexed)},{WorkspaceNameText(moved.Workspace, indexed)},{EventMonitor(state, moved.Workspace)}");
                break;
            case FloatingChanged floating:
                lines.Add($"changefloatingmode>>{EventAddress(floating.Window)},{(floating.Floating ? "1" : "0")}");
                break;
            case ClientMinimized minimized:
                lines.Add($"minimized>>{EventAddress(minimized.Window)},{(minimized.Minimized ? "1" : "0")}");
                break;
            case ClientPinned pinned:
                lines.Add($"pin>>{EventAddress(pinned.Window)},{(pinned.Pinned ? "1" : "0")}");
                break;
            case GroupToggled toggled:
                lines.Add($"togglegroup>>{(toggled.Created ? "1" : "0")},{EventAddress(toggled.Window)}");
                break;
            case ClientIntoGroup into:
                lines.Add("moveintogroup>>" + EventAddress(into.Window));
                break;
            case ClientOutOfGroup outOf:
                lines.Add("moveoutofgroup>>" + EventAddress(outOf.Window));
                break;
            case WorkspaceActivated activated:
                lines.Add($"workspace>>{WorkspaceNameText(activated.Workspace, indexed)},{EventMonitor(state, activated.Workspace)}");
                lines.Add($"workspacev2>>{WorkspaceIdText(activated.Workspace, indexed)},{WorkspaceNameText(activated.Workspace, indexed)},{EventMonitor(state, activated.Workspace)}");
                break;
            case WorkspaceCreated created:
                lines.Add($"createworkspace>>{WorkspaceNameText(created.Workspace, indexed)},{EventMonitor(state, created.Workspace)}");
                lines.Add($"createworkspacev2>>{WorkspaceIdText(created.Workspace, indexed)},{WorkspaceNameText(created.Workspace, indexed)},{EventMonitor(state, created.Workspace)}");
                break;
            case WorkspaceDestroyed destroyed:
                lines.Add($"destroyworkspace>>{Clean(destroyed.Name)},{Clean(destroyed.Monitor)}");
                lines.Add($"destroyworkspacev2>>{destroyed.Id.ToString(CultureInfo.InvariantCulture)},{Clean(destroyed.Name)},{Clean(destroyed.Monitor)}");
                break;
            case WorkspaceMoved movedWorkspace:
                lines.Add($"moveworkspace>>{WorkspaceNameText(movedWorkspace.Workspace, indexed)},{Clean(movedWorkspace.Monitor)}");
                lines.Add($"moveworkspacev2>>{WorkspaceIdText(movedWorkspace.Workspace, indexed)},{WorkspaceNameText(movedWorkspace.Workspace, indexed)},{Clean(movedWorkspace.Monitor)}");
                break;
            case MonitorFocused focusedMonitor:
                lines.Add($"focusedmon>>{Clean(focusedMonitor.Name)},{WorkspaceNameText(focusedMonitor.Workspace, indexed)}");
                lines.Add($"focusedmonv2>>{Clean(focusedMonitor.Name)},{WorkspaceIdText(focusedMonitor.Workspace, indexed)}");
                break;
            case FullscreenChanged fullscreen:
                lines.Add("fullscreen>>" + (fullscreen.Entered ? "1" : "0"));
                break;
            case ActiveSpecial special:
                if (special.Workspace is WorkspaceId specialWorkspace)
                {
                    lines.Add($"activespecial>>{Clean(WorkspaceNameText(specialWorkspace, indexed))},{Clean(special.Monitor)}");
                    lines.Add($"activespecialv2>>{WorkspaceIdText(specialWorkspace, indexed)},{Clean(WorkspaceNameText(specialWorkspace, indexed))},{Clean(special.Monitor)}");
                }
                else
                {
                    lines.Add($"activespecial>>,{Clean(special.Monitor)}");
                    lines.Add($"activespecialv2>>,,{Clean(special.Monitor)}");
                }
                break;
            case ConfigWarned warned:
                lines.Add($"warning>>{warned.Line.ToString(CultureInfo.InvariantCulture)},{Clean(warned.Source)},{Clean(warned.Message)}");
                break;
            case ConfigReloaded:
                lines.Add("configreloaded>>");
                break;
            case MonitorAdded added:
                lines.Add($"monitoradded>>{Clean(added.Name)}");
                break;
            case MonitorAddedV2 added:
                lines.Add($"monitoraddedv2>>{added.Id.ToString(CultureInfo.InvariantCulture)},{Clean(added.Name)},{Clean(added.Description)}");
                break;
            case MonitorRemoved removed:
                lines.Add($"monitorremoved>>{Clean(removed.Name)}");
                break;
            case MonitorRemovedV2 removed:
                lines.Add($"monitorremovedv2>>{removed.Id.ToString(CultureInfo.InvariantCulture)},{Clean(removed.Name)},{Clean(removed.Description)}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(streamEvent), streamEvent, "No format for this event.");
        }
    }

    private static void AppendFocus(List<string> lines, FocusChanged focused, State state)
    {
        if (focused.Window is not WindowId window)
        {
            lines.Add("activewindow>>,");
            lines.Add("activewindowv2>>");
            return;
        }

        var klass = "";
        var title = "";
        foreach (var client in state.Clients)
        {
            if (client.Window != window)
                continue;

            klass = client.Class;
            title = client.Title;
            break;
        }

        lines.Add($"activewindow>>{Clean(klass)},{Clean(title)}");
        lines.Add("activewindowv2>>" + EventAddress(window));
    }

    internal static string EventAddress(WindowId window) =>
        window.Value.ToString("x", CultureInfo.InvariantCulture);

    private static string EventMonitor(State state, WorkspaceId id) => Clean(MonitorName(state, id));

    private static string TitleOf(State state, WindowId window)
    {
        foreach (var client in state.Clients)
        {
            if (client.Window == window)
                return client.Title;
        }

        return "";
    }
}
