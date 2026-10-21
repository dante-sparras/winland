using static Winland.OrdinalText;

namespace Winland;

public sealed partial class Core
{
    private readonly record struct Captured(State State, WindowId? Active);

    private Captured Capture() => new(Snapshot(), ActiveWindow());

    // Workspaces changeworkspaceid gave a new id during this input, oldest first.
    private List<(WorkspaceId From, WorkspaceId To)>? _readdressed;

    // Workspaces reported as active for this input, oldest first. A report is a Monitor's shown Workspace
    // changing while the focus may follow, and the Workspace a swap leaves on the focused Monitor. Moving the
    // focus onto a Monitor that already shows its Workspace is not a report, and neither is a change on a
    // Monitor that is not focused, nor a Monitor coming or going.
    private List<WorkspaceId>? _activated;

    private void NoteWorkspaceActive(WorkspaceId workspace) => (_activated ??= []).Add(workspace);

    private Outcome WithEvents(Outcome outcome, Captured before, Input? input)
    {
        // A Workspace that only changed its id is the same Workspace, so it is compared under its new id and emits
        // neither a destroy nor a create, nor moves its Clients or the cursor.
        if (_readdressed is { } readdressed)
        {
            _readdressed = null;
            foreach (var (from, to) in readdressed)
                before = before with { State = Readdressed(before.State, from, to) };
        }

        outcome = FinishCursor(before, outcome, input);
        List<WorkspaceId>? activated = null;
        if (_activated is { Count: > 0 })
        {
            activated = _activated;
            _activated = null;
        }

        var activeAfter = ActiveWindow();
        var reason = FocusReason.Unknown;
        if (activeAfter != before.Active)
        {
            // One input emits one focus event, for the window that ends focused. A handler that knows a reason the
            // dispatcher name does not say, such as the pointer, records it. Otherwise the input and the dispatcher do.
            reason = _focusReason == FocusReason.Unknown
                ? ReasonFor(input, outcome, before, activeAfter)
                : _focusReason;
        }

        _focusReason = FocusReason.Unknown;
        var events = Diff(before.State, outcome.State, before.Active, activeAfter, reason, activated);
        if (_configEvents is not null)
        {
            events.AddRange(_configEvents);
            _configEvents = null;
        }

        if (_extraEvents is { Count: > 0 })
        {
            events.AddRange(_extraEvents);
            _extraEvents = null;
        }

        if (_noted is not null)
        {
            events.AddRange(_noted);
            _noted = null;
        }

        if (_modalRules is { Count: > 0 } modal)
        {
            _modalRules = null;
            AddModalRules(events, modal, outcome.State);
        }

        return events.Count == 0 ? outcome : outcome with { Events = events };
    }

    private static State Readdressed(State state, WorkspaceId from, WorkspaceId to)
    {
        WorkspaceId Map(WorkspaceId id) => id == from ? to : id;
        WorkspaceId? MapOptional(WorkspaceId? id) => id is WorkspaceId some ? Map(some) : null;
        return state with
        {
            Clients = [.. state.Clients.Select(client => client with { Workspace = Map(client.Workspace) })],
            Hidden = [.. state.Hidden.Select(client => client with { Workspace = Map(client.Workspace) })],
            Workspaces = [.. state.Workspaces.Select(workspace => workspace with { Id = Map(workspace.Id) })],
            ActiveWorkspace = Map(state.ActiveWorkspace),
            SpecialWorkspace = MapOptional(state.SpecialWorkspace),
            Screens = [.. state.Screens.Select(screen => screen with
            {
                ActiveWorkspace = Map(screen.ActiveWorkspace),
                SpecialWorkspace = MapOptional(screen.SpecialWorkspace),
            })],
        };
    }

    private static List<StreamEvent> Diff(State before, State after, WindowId? activeBefore, WindowId? activeAfter, FocusReason reason, IReadOnlyList<WorkspaceId>? activated)
    {
        var events = new List<StreamEvent>();
        AddWorkspaceEvents(events, before, after);
        var previous = ByWindow(before);
        var next = ByWindow(after);
        var hiddenBefore = HiddenWindows(before);
        var hiddenAfter = HiddenWindows(after);
        var rulesRead = new HashSet<WindowId>();
        foreach (var pair in previous)
        {
            if (!next.ContainsKey(pair.Key))
                events.Add(new ClientClosed(pair.Key));
        }

        foreach (var pair in next)
        {
            if (!previous.TryGetValue(pair.Key, out var prior))
            {
                events.Add(new ClientOpened(pair.Key, pair.Value.Workspace, pair.Value.Class, pair.Value.Title));
                if (pair.Value.Fullscreen != FullscreenMode.None)
                    events.Add(new FullscreenChanged(true, pair.Key));
                // A window that has just mapped has its rules read, unless the workspace it mapped on is hidden.
                if (RulesAreRead(after, pair.Value))
                    TellRules(events, rulesRead, pair.Key);
                continue;
            }

            if (prior.Workspace != pair.Value.Workspace)
                events.Add(new ClientMoved(pair.Key, pair.Value.Workspace));

            if (prior.Title != pair.Value.Title)
                events.Add(new ClientRetitled(pair.Key, pair.Value.Title, pair.Key == activeAfter));

            if (prior.Class != pair.Value.Class)
                events.Add(new ClientReclassed(pair.Key, pair.Value.Class, pair.Key == activeAfter));

            if (prior.Floating != pair.Value.Floating)
                events.Add(new FloatingChanged(pair.Key, pair.Value.Floating));

            if (prior.Minimized != pair.Value.Minimized)
                events.Add(new ClientMinimized(pair.Key, pair.Value.Minimized));

            if (prior.Fullscreen != pair.Value.Fullscreen)
                events.Add(new FullscreenChanged(pair.Value.Fullscreen != FullscreenMode.None, pair.Key));

            if (RulesAreRead(after, pair.Value) && RulesChanged(prior, pair.Value, before, after, hiddenBefore, hiddenAfter))
                TellRules(events, rulesRead, pair.Key);
        }

        if (after.ActiveMonitor != before.ActiveMonitor)
        {
            var name = MonitorName(after, after.ActiveMonitor);
            if (name.Length > 0)
                events.Add(new MonitorFocused(name, WorkspaceOn(after, after.ActiveMonitor)));
        }

        if (activated is not null)
        {
            foreach (var workspace in activated)
                events.Add(new WorkspaceActivated(workspace));
        }

        if (activeAfter != activeBefore)
        {
            // Focus is a rule match. The window that lost it and the window that gained it are both read again.
            if (activeBefore is WindowId lost && next.TryGetValue(lost, out var loser) && RulesAreRead(after, loser))
                TellRules(events, rulesRead, lost);
            if (activeAfter is WindowId gained && next.TryGetValue(gained, out var winner) && RulesAreRead(after, winner))
                TellRules(events, rulesRead, gained);
            events.Add(new FocusChanged(activeAfter) { Reason = reason });
        }

        AddSpecialEvents(events, before, after);
        AddMonitorEvents(events, before, after);
        return events;
    }

    private static void AddWorkspaceEvents(List<StreamEvent> events, State before, State after)
    {
        var previous = new Dictionary<WorkspaceId, Workspace>(before.Workspaces.Count);
        foreach (var workspace in before.Workspaces)
            previous[workspace.Id] = workspace;

        var next = new HashSet<WorkspaceId>();
        foreach (var workspace in after.Workspaces)
            next.Add(workspace.Id);

        foreach (var workspace in after.Workspaces)
        {
            if (!previous.ContainsKey(workspace.Id))
                events.Add(new WorkspaceCreated(workspace.Id));
        }

        foreach (var workspace in before.Workspaces)
        {
            if (next.Contains(workspace.Id))
                continue;

            // The name is read before the Workspace is gone, so its defaultName is still reported.
            events.Add(new WorkspaceDestroyed(
                workspace.Id,
                CommandProtocol.WorkspaceNumber(before, workspace.Id),
                workspace.DisplayName ?? workspace.Id.Name,
                MonitorName(before, workspace.Monitor)));
        }

        foreach (var workspace in after.Workspaces)
        {
            if (!previous.TryGetValue(workspace.Id, out var prior) || prior.Monitor == workspace.Monitor)
                continue;

            var name = MonitorName(after, workspace.Monitor);
            if (name.Length == 0)
                continue;

            events.Add(new WorkspaceMoved(workspace.Id, name));
        }
    }

    private static string MonitorName(State state, MonitorId id)
    {
        foreach (var screen in state.Screens)
        {
            if (screen.Id == id)
                return screen.Monitor.Name;
        }

        return "";
    }

    private static WorkspaceId WorkspaceOn(State state, MonitorId id)
    {
        foreach (var screen in state.Screens)
        {
            if (screen.Id == id)
                return screen.ActiveWorkspace;
        }

        return state.ActiveWorkspace;
    }

    private static void AddMonitorEvents(List<StreamEvent> events, State before, State after)
    {
        var count = events.Count;
        AddPlugEvents(events, before, after);
        if (events.Count > count || Arranged(before, after))
            events.Add(new MonitorLayoutChanged());
    }

    // Whether a monitor kept from before has another mode, position, scale, orientation or mirror, which the pinned
    // release lays the monitors out again for. Its work area is not its layout.
    private static bool Arranged(State before, State after)
    {
        foreach (var screen in after.Screens)
        {
            foreach (var old in before.Screens)
            {
                if (!old.Monitor.Name.Equals(screen.Monitor.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (old.Monitor with { WorkArea = default } != screen.Monitor with { WorkArea = default })
                    return true;
            }
        }

        return false;
    }

    private static void AddPlugEvents(List<StreamEvent> events, State before, State after)
    {
        var nextNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in after.Screens)
            nextNames.Add(screen.Monitor.Name);

        foreach (var screen in before.Screens)
        {
            if (nextNames.Contains(screen.Monitor.Name))
                continue;

            events.Add(new MonitorRemoved(screen.Monitor.Name));
            events.Add(new MonitorRemovedV2(screen.Id.Value, screen.Monitor.Name, screen.Monitor.Description));
        }

        var previous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in before.Screens)
            previous.Add(screen.Monitor.Name);

        foreach (var screen in after.Screens)
        {
            if (previous.Contains(screen.Monitor.Name))
                continue;

            events.Add(new MonitorAdded(screen.Monitor.Name));
            events.Add(new MonitorAddedV2(screen.Id.Value, screen.Monitor.Name, screen.Monitor.Description));
        }
    }

    // A hidden workspace is not read, as Winland skips a hidden window. A member waiting in a group is
    // still on the shown workspace, so it is read. A pinned Client stays shown on every workspace of its monitor.
    private static bool RulesAreRead(State state, Client client)
    {
        if (client.Pinned)
            return true;

        if (client.Workspace == state.ActiveWorkspace || client.Workspace == state.SpecialWorkspace)
            return true;

        foreach (var screen in state.Screens)
        {
            if (client.Workspace == screen.ActiveWorkspace || client.Workspace == screen.SpecialWorkspace)
                return true;
        }

        return false;
    }

    private static void TellRules(List<StreamEvent> events, HashSet<WindowId> told, WindowId window)
    {
        if (told.Add(window))
            events.Add(new ClientRulesUpdated(window));
    }

    private static void AddModalRules(List<StreamEvent> events, List<WindowId> modal, State state)
    {
        var told = new HashSet<WindowId>();
        foreach (var streamEvent in events)
        {
            if (streamEvent is ClientRulesUpdated updated)
                told.Add(updated.Window);
        }

        var clients = ByWindow(state);
        foreach (var window in modal)
        {
            if (clients.TryGetValue(window, out var client) && RulesAreRead(state, client))
                TellRules(events, told, window);
        }
    }

    private static bool RulesChanged(Client prior, Client next, State before, State after, HashSet<WindowId> hiddenBefore, HashSet<WindowId> hiddenAfter)
    {
        if (prior.Class != next.Class
            || prior.Title != next.Title
            || prior.Floating != next.Floating
            || prior.Fullscreen != next.Fullscreen
            || prior.FullscreenClient != next.FullscreenClient
            || prior.Workspace != next.Workspace
            || prior.Pinned != next.Pinned
            || prior.Content != next.Content
            || !SameTags(prior.Tags, next.Tags))
            return true;

        var membersBefore = MembersOf(before, prior.Window);
        var membersAfter = MembersOf(after, next.Window);
        if (!SameMembers(membersBefore, membersAfter))
            return true;

        if (membersAfter is null)
            return false;

        var shownBefore = ShownMember(membersBefore, hiddenBefore);
        var shownAfter = ShownMember(membersAfter, hiddenAfter);
        return shownBefore != shownAfter && next.Window == shownAfter;
    }

    private static bool SameTags(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        var leftCount = left?.Count ?? 0;
        var rightCount = right?.Count ?? 0;
        if (leftCount != rightCount)
            return false;

        for (var i = 0; i < leftCount; i++)
        {
            if (!string.Equals(left![i], right![i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static IReadOnlyList<WindowId>? MembersOf(State state, WindowId window)
    {
        foreach (var group in state.Groups)
        {
            foreach (var member in group)
            {
                if (member == window)
                    return group;
            }
        }

        return null;
    }

    private static bool SameMembers(IReadOnlyList<WindowId>? left, IReadOnlyList<WindowId>? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }

    private static HashSet<WindowId> HiddenWindows(State state)
    {
        var hidden = new HashSet<WindowId>(state.Hidden.Count);
        foreach (var client in state.Hidden)
            hidden.Add(client.Window);
        return hidden;
    }

    // The shown member is the one group member that is not hidden. None, when the whole workspace is hidden.
    private static WindowId? ShownMember(IReadOnlyList<WindowId>? members, HashSet<WindowId> hidden)
    {
        if (members is null)
            return null;

        WindowId? shown = null;
        foreach (var member in members)
        {
            if (hidden.Contains(member))
                continue;

            if (shown is not null)
                return null;

            shown = member;
        }

        return shown;
    }

    private static Dictionary<WindowId, Client> ByWindow(State state)
    {
        var map = new Dictionary<WindowId, Client>(state.Clients.Count);
        foreach (var client in state.Clients)
            map[client.Window] = client;

        return map;
    }

    // The reason of the focus this input left, as Winland reports the last reason for that window.
    private FocusReason ReasonFor(Input? input, Outcome outcome, Captured before, WindowId? after)
    {
        if (input is WindowFocused focused)
            return ReasonForOs(focused);

        if (input is WindowAppeared)
            return FocusReason.NewWindow;

        if (input is WindowClosed closed)
            return ReasonForClose(closed.Window, after, before.State);

        // A pointer move that is not a drag follows the window under it. A drag still has its gesture or border.
        if (input is PointerMoved && _gesture is null && _border is null)
            return FocusReason.FollowMouse;

        if (outcome.Dispatcher is string name)
            return ReasonForDispatcher(name, input, before, after, outcome.State);

        if (input is PointerMoved or PointerButtonPressed or PointerButtonReleased or PointerGesture)
            return FocusReason.DesktopState;

        return FocusReason.Unknown;
    }

    // A click on the window is a click. An activation Windows accepted, after a click in another program, is a
    // desktop-state change. A confirmation Winland asked for, or a change with no equivalent, is unknown.
    private FocusReason ReasonForOs(WindowFocused focused)
    {
        if (ActivatedAfterClick(focused))
            return FocusReason.DesktopState;

        if (focused.Cause == FocusCause.User && focused.Clicked is not null)
            return FocusReason.Click;

        return FocusReason.Unknown;
    }

    private static FocusReason ReasonForClose(WindowId closed, WindowId? after, State before)
    {
        if (after is null)
            return FocusReason.Other;

        if (SameGroup(before, closed, after.Value))
            return FocusReason.UnmapGrouped;

        return ClientIn(before, closed) is { Floating: true } ? FocusReason.UnmapFloating : FocusReason.UnmapTiled;
    }

    private FocusReason ReasonForDispatcher(string name, Input? input, Captured before, WindowId? after, State afterState)
    {
        if (Is(name, "focuswindow"))
            return FocusReason.FocusWindow;

        if (Is(name, "moveintogroup") || Is(name, "moveintoorcreategroup"))
            return FocusReason.MoveIntoGroup;

        if (Is(name, "changegroupactive"))
            return FocusReason.GroupCurrent;

        if (Is(name, "togglespecialworkspace"))
            return FocusReason.ToggleSpecial;

        if (Is(name, "workspace"))
            return before.State.ActiveMonitor != afterState.ActiveMonitor ? FocusReason.Keybind : FocusReason.Workspace;

        if (Is(name, "movefocus"))
            return ReasonForMoveFocus(before, after, afterState);

        if (Is(name, "cyclenext") || Is(name, "focuscurrentorlast") || Is(name, "focusurgentorlast"))
            return ReasonForSwitch(before, after, afterState);

        if (Is(name, "focusmonitor") || Is(name, "movetoworkspace") || Is(name, "movetoworkspacesilent")
            || Is(name, "moveoutofgroup"))
            return FocusReason.Keybind;

        if (Is(name, "swapactiveworkspaces") || Is(name, "movegroupwindow"))
            return FocusReason.DesktopState;

        if (Is(name, "layoutmsg") || Is(name, "layout"))
            return LayoutFocusReason();

        // A drag begins, moves and ends as the pointer, and reports a desktop-state change. Typing the dispatcher does not.
        if ((Is(name, "movewindow") || Is(name, "resizewindow"))
            && input is PointerMoved or PointerButtonPressed or PointerButtonReleased or PointerGesture)
            return FocusReason.DesktopState;

        return FocusReason.Unknown;
    }

    private static FocusReason ReasonForMoveFocus(Captured before, WindowId? after, State afterState)
    {
        if (SameGroup(before.State, before.Active, after))
            return FocusReason.GroupCurrent;

        if (before.State.ActiveMonitor != afterState.ActiveMonitor)
            return FocusReason.Keybind;

        return ReasonForSwitch(before, after, afterState);
    }

    // A switch on one workspace is hard when the window left fills the monitor and the layout does not hold that
    // fullscreen. Maximized is soft. One onto a workspace that was not shown is the workspace change, or a keybind
    // when that workspace is on another monitor, because the original switch is not what gets reported.
    private static FocusReason ReasonForSwitch(Captured before, WindowId? after, State afterState)
    {
        if (before.Active is not WindowId from || after is not WindowId to
            || ClientIn(before.State, from) is not { } left
            || ClientIn(afterState, to) is not { } right)
            return FocusReason.SwitchSoft;

        if (left.Workspace != right.Workspace)
            return before.State.ActiveMonitor != afterState.ActiveMonitor ? FocusReason.Keybind : FocusReason.Workspace;

        if (left.Fullscreen == FullscreenMode.Fullscreen && !left.LayoutFullscreen)
            return FocusReason.SwitchHard;

        return FocusReason.SwitchSoft;
    }

    // Master cycles with a keybind. Monocle reveals the next tile as a desktop-state change.
    private FocusReason LayoutFocusReason()
    {
        var layout = LayoutFactory.NameOf(CurrentWorkspace().Layout);
        if (layout == "monocle")
            return FocusReason.DesktopState;

        if (layout == "master")
            return FocusReason.Keybind;

        return FocusReason.Unknown;
    }

    private static bool SameGroup(State state, WindowId? left, WindowId? right)
    {
        if (left is not WindowId first || right is not WindowId second || first == second)
            return false;

        foreach (var group in state.Groups)
        {
            var hasFirst = false;
            var hasSecond = false;
            foreach (var member in group)
            {
                if (member == first)
                    hasFirst = true;
                if (member == second)
                    hasSecond = true;
            }

            if (hasFirst && hasSecond)
                return true;
        }

        return false;
    }

    private static Client? ClientIn(State state, WindowId window)
    {
        foreach (var client in state.Clients)
        {
            if (client.Window == window)
                return client;
        }

        foreach (var client in state.Hidden)
        {
            if (client.Window == window)
                return client;
        }

        return null;
    }
}
