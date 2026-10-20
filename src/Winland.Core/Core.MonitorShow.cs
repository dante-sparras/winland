namespace Winland;

// setworkspace and setspecialworkspace on the pipe: a Monitor shows a Workspace, or opens or closes a special one, without
// the focus going to that Monitor, as the pinned release's Lua monitor objects do. Only on the focused Monitor does the
// focus follow, and the cursor stays where it is.
public sealed partial class Core
{
    // Set while one of these runs, so the change of Workspace moves no cursor.
    private bool _holdCursor;

    /// <summary>
    /// <c>setworkspace MONITOR SELECTOR</c>: shows the Workspace the selector names, read from that Monitor, on it. It is
    /// made when missing and moved there from another Monitor. A special Workspace is left alone.
    /// </summary>
    private CommandReply SetWorkspaceOn(string arguments)
    {
        if (!TryMonitorAndRest(arguments, out var screen, out var rest) || screen is null)
            return Refuse("error: monitor not found");

        if (rest.Length == 0 || !WorkspaceAddress.TryResolve(rest, Context(screen.Id), out var resolved))
            return Refuse("error: invalid workspace");

        var target = Alias(resolved);
        if (target.IsSpecial)
            return new CommandReply("ok", View([]));

        // The Workspace this Monitor shows is reported only when the focus may follow, so a Monitor that is not
        // focused reports nothing for itself. Taking the Workspace the focused Monitor shows reports the one it
        // shows next.
        var focusedBefore = ActiveOn(FocusedId());
        var before = ShowingNow();
        var focused = screen.Id == FocusedId();
        var desk = WorkspaceFor(target, screen.Id);
        if (desk.Monitor != screen.Id)
            HandWorkspace(desk, screen);

        WindowId? focus = null;
        var hidden = Array.Empty<WindowId>();
        if (screen.Active != target)
        {
            var sourceId = screen.Active;
            screen.Active = target;
            _activeOn[screen.Id] = target;
            if (sourceId is WorkspaceId source)
                CarryPinned(source, desk);

            if (focused)
            {
                Remember(target, screen.Id);
                hidden = SpecialHiddenBy(screen.Id, focus: null);
                if (SpecialOn(screen.Id) is null)
                {
                    focus = Accept(desk.Remembered());
                    _active = focus;
                    desk.Focus = focus;
                    if (focus is WindowId noted)
                        desk.Layout.NoteFocus(noted);
                }

                _holdCursor = true;
            }

            if (sourceId is WorkspaceId left)
                DropIfEmpty(left);
        }

        SyncActive();
        var focusedNow = ActiveOn(FocusedId());
        if (focusedNow != focusedBefore)
            NoteWorkspaceActive(focusedNow);

        var (show, hide) = VisibilityDelta(before);
        return new CommandReply("ok", View(ShownPlacements(), show, Combine(hide, hidden), focus: focus));

        CommandReply Refuse(string error) => new(error, View([]) with { Error = error });
    }

    /// <summary>
    /// <c>setspecialworkspace MONITOR [SELECTOR]</c>: opens the special Workspace the selector names on that Monitor, taking
    /// it from another Monitor that has it open. Without a selector it closes the one open there.
    /// </summary>
    private CommandReply SetSpecialOn(string arguments)
    {
        if (!TryMonitorAndRest(arguments, out var screen, out var rest) || screen is null)
            return Refuse("error: monitor not found");

        WorkspaceId? target = null;
        if (rest.Length > 0)
        {
            if (!WorkspaceAddress.TryResolve(rest, Context(screen.Id), out var resolved) || !resolved.IsSpecial)
                return Refuse("error: invalid workspace");

            target = resolved;
        }

        if (SpecialOn(screen.Id) == target)
            return new CommandReply("ok", View([]));

        var before = ShowingNow();
        var focusedId = FocusedId();
        WindowId? focus = null;
        if (target is not WorkspaceId opening)
        {
            focus = Conceal(screen.Id);
        }
        else
        {
            if (MonitorShowing(opening) is MonitorId other && other != screen.Id)
                focus = Conceal(other);

            var previous = SpecialOn(screen.Id);
            var desk = WorkspaceFor(opening, screen.Id);
            if (desk.Monitor != screen.Id)
            {
                desk.Monitor = screen.Id;
                Refit(desk);
            }

            SetSpecial(screen.Id, opening);
            BringFloatingOnto(desk, screen.Id);
            if (previous is WorkspaceId closed)
                DropIfEmpty(closed);

            if (screen.Id == focusedId)
            {
                var next = desk.Remembered();
                if (next is null && _input.SpecialFallthrough)
                    next = FocusedWorkspace().Remembered();

                _active = next;
                desk.Focus = next;
                focus = next;
            }
        }

        _holdCursor = true;
        var (show, hide) = VisibilityDelta(before);
        return new CommandReply("ok", View(ShownPlacements(), show, hide, focus: focus));

        // Closes the special Workspace open on a Monitor. The focus goes back to that Monitor's Workspace only when the
        // Monitor is the focused one.
        WindowId? Conceal(MonitorId monitor)
        {
            var kept = _active;
            _ = ConcealSpecial(monitor);
            if (monitor == focusedId)
                return _active;

            _active = kept;
            return null;
        }

        CommandReply Refuse(string error) => new(error, View([]) with { Error = error });
    }

    // Moves a Workspace onto a Monitor that does not show it yet, as moveworkspacetomonitor does, without the focus
    // following it. The Monitor it left shows another Workspace, and a focused window that went with it gives the
    // focus to that one.
    private void HandWorkspace(LiveWorkspace desk, LiveMonitor destination)
    {
        var origin = MonitorById(desk.Monitor);
        var wasActive = origin is not null && origin.Active == desk.Id;
        var dx = destination.Effective.X - (origin?.Effective.X ?? destination.Effective.X);
        var dy = destination.Effective.Y - (origin?.Effective.Y ?? destination.Effective.Y);
        var hadFocus = _active is WindowId active && desk.ClientOf(active) is not null;
        if (wasActive && origin is not null)
            Plug(origin, desk);

        desk.Layout.NudgeFloating(dx, dy);
        desk.Monitor = destination.Id;
        Refit(desk);
        if (!hadFocus || origin is not { Focused: true } || origin.Active is not WorkspaceId shown || FindWorkspace(shown) is not LiveWorkspace replacement)
            return;

        var next = Accept(replacement.Remembered());
        _active = next;
        replacement.Focus = next;
        if (next is WindowId noted)
            replacement.Layout.NoteFocus(noted);
    }

    private bool TryMonitorAndRest(string arguments, out LiveMonitor? screen, out string rest)
    {
        var text = arguments.Trim();
        var space = text.IndexOfAny([' ', '\t']);
        var name = space < 0 ? text : text[..space];
        rest = space < 0 ? "" : text[(space + 1)..].Trim();
        screen = name.Length == 0 ? null : MonitorOf(MonitorSelector.Resolve(name, Targets(), TargetOf(FocusedMonitor())));
        return screen is not null;
    }
}
