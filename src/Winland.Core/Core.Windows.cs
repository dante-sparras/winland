namespace Winland;

public sealed partial class Core
{
    private static IReadOnlyList<WindowRule> JoinRules(IReadOnlyList<WindowRule> rules, IReadOnlyList<WindowRule> followed)
    {
        if (followed.Count == 0)
            return rules;
        if (rules.Count == 0)
            return followed;

        var combined = new WindowRule[rules.Count + followed.Count];
        for (var i = 0; i < rules.Count; i++)
            combined[i] = rules[i];
        for (var i = 0; i < followed.Count; i++)
            combined[rules.Count + i] = followed[i];
        return combined;
    }

    private WindowId? ActiveWindow()
    {
        if (_active is not WindowId active)
            return null;

        if (ClientOn(ActiveId(), active) is not null)
            return active;

        var desk = WorkspaceContaining(active);
        if (desk is null || !IsShown(desk.Id))
            return null;

        return desk.ClientOf(active) is { Minimized: false } ? active : null;
    }

    private void Note(Input input)
    {
        switch (input)
        {
            case WindowAppeared appeared when WorkspaceContaining(appeared.Window) is { } appearedWorkspace
                && IsShown(appearedWorkspace.Id)
                && !_awaitingMapFocus.Contains(appeared.Window):
                var current = SpecialOn(appearedWorkspace.Monitor) ?? ActiveOn(appearedWorkspace.Monitor);
                if (appearedWorkspace.Id == current)
                {
                    appearedWorkspace.Focus = appeared.Window;
                    if (_monitors.Count == 0 || appearedWorkspace.Monitor == FocusedId())
                        _active = appeared.Window;
                    TouchHistory(appeared.Window);
                }

                break;
        }
    }

    private void NoteFocus(WindowId window)
    {
        var owner = WorkspaceContaining(window);
        if (owner is not null && IsShown(owner.Id))
        {
            if (_monitors.Count > 0)
                Focus(owner.Monitor);

            var special = SpecialOn(owner.Monitor);
            var onSpecial = special == owner.Id;
            var onNormal = owner.Id == ActiveOn(owner.Monitor);
            if (onSpecial || (onNormal && (special is null || _input.SpecialFallthrough)))
            {
                _active = window;
                owner.Focus = window;
                TouchHistory(window);
            }

            return;
        }

        if (Find(window) is null)
            _active = null;
    }

    private Outcome Appear(WindowAppeared appeared)
    {
        if (IsSwallowedParent(appeared.Window))
            return View([], hide: [appeared.Window]);

        var processId = appeared.ProcessId is > 0 and <= int.MaxValue ? (int)appeared.ProcessId : 0;
        if (processId > 0)
        {
            _follow.NoteLineage(processId, appeared.AncestorProcessIds);
            _initialWorkspaces.NoteLineage(processId, appeared.AncestorProcessIds);
        }

        var followed = processId > 0 ? _follow.Take(processId, FollowNow()) : [];
        var decision = WindowRuleParser.Decide(appeared.Traits, JoinRules(_rules, followed));
        if (!decision.Adopt)
            return View([]);

        if (Find(appeared.Window) is { } existing)
            return IsShown(existing.Workspace) && !IsWaiting(appeared.Window) ? View([]) : View([], hide: [appeared.Window]);

        var restored = FindRestored(appeared.Window);
        decision = Recovered(decision, restored);
        var swallowee = Swallowee(appeared);
        var screen = MonitorFor(appeared);
        if (_monitors.Count > 0 && screen is null && !RestoredMonitorExists(restored))
            return View([]);

        if (restored is null)
            screen = RuleMonitor(decision.Placement.Monitor, screen);

        var monitor = RestoredMonitor(restored, screen);
        var active = screen is null ? ActiveOn(monitor) : screen.Active ?? ActiveOn(monitor);
        var current = SpecialOn(monitor) ?? active;
        var rule = WorkspaceAddress.ParseRule(decision.Workspace, Context(monitor));
        if (rule.Id is WorkspaceId addressed)
            rule = rule with { Id = Alias(addressed) };

        // misc:initial_workspace_tracking: a program Winland started opens its window, silently, on the Workspace it was
        // started on. A workspace rule still wins, and a window that comes back after a restart keeps its own.
        if (restored is null && _session.InitialWorkspaceTracking > 0 && processId > 0)
        {
            var started = _initialWorkspaces.Take(processId, appeared.Window, _session.InitialWorkspaceTracking == 2, FollowNow(), window => Find(window)?.Workspace);
            if (rule.Id is null && started is WorkspaceId origin && origin != current)
                rule = new WorkspaceAddress.Rule(origin, Silent: true);
        }
        var target = restored?.Workspace ?? rule.Id ?? current;
        ForgetRestored(appeared.Window);
        var desk = WorkspaceFor(target, monitor);
        var area = WorkArea(desk, appeared.WorkArea);
        _area = area;
        var bounds = restored is null ? PlaceOnMap(appeared, decision, desk, area) : restored.Bounds ?? appeared.Bounds;
        var covered = desk.Covered();
        if (decision.ScrollingWidth is double scrollingWidth)
            desk.Layout.NoteScrollingWidth(appeared.Window, scrollingWidth);
        var placements = desk.Layout.Add(
            appeared.Window,
            area,
            appeared.Pointer,
            decision.Floating,
            bounds,
            target,
            appeared.Traits.Class,
            appeared.Traits.Title,
            IsShown(target),
            FocusedMonitorBounds(),
            appeared.ProcessId,
            minSize: decision.MinSize,
            maxSize: decision.MaxSize,
            tiledLimits: RememberSizes(appeared.Window, decision.Sizes, appeared.Traits.MinSize, appeared.Traits.MaxSize));
        if (decision.FullscreenRequest is { } fullscreen)
            placements = desk.Layout.SetFullscreen(appeared.Window, fullscreen.Internal, fullscreen.Client);
        else
            placements = OpenUnderFullscreen(desk, appeared.Window, placements);

        // A window joins the Group focused when it opened even when it swallows its parent, as in the pinned
        // release. Only a window that took its parent's place in a Group stays out of the focused one.
        var focused = restored is null ? ActiveWindow() : null;
        if (swallowee is not null)
            placements = SwallowParent(appeared.Window, swallowee, desk, placements, decision.Group);
        var slotted = _swallows.TryGetValue(appeared.Window, out var swallow) && swallow.Slot;
        if (GroupOnOpen(desk, appeared.Window, decision, appeared.Traits.Dialog, slotted ? null : focused))
            placements = desk.Layout.Visible();

        NotePlaced(appeared.Window, desk, decision, restored is not null);
        RememberFocus(appeared.Window, decision);
        RememberOpacity(appeared.Window, decision);
        RememberIdle(appeared.Window, decision);
        if (decision.Pin && decision.Floating)
            _pinned.Add(appeared.Window);
        var restore = _awaitingMapFocus.Contains(appeared.Window) ? Kept() : null;
        if (restore == appeared.Window)
            restore = null;

        if (restored is null && rule.Id is not null && !rule.Silent && !IsShown(target) && !_awaitingMapFocus.Contains(appeared.Window))
        {
            if (MonitorById(desk.Monitor) is { } owner)
                Focus(owner.Id);

            if (target.IsSpecial)
                return OpenSpecial(target, appeared.Window, null, allowsElevation: false);

            return SwitchTo(target, appeared.Window, consumed: false, dispatcher: null);
        }

        if (!IsShown(target))
        {
            if (_pinned.Contains(appeared.Window) && MonitorShowing(desk.Monitor))
                return Offering(View(placements, show: [appeared.Window]), restore);

            return Offering(View([], hide: [appeared.Window]), restore);
        }

        return Offering(Present(desk, covered, placements), restore);
    }

    private Outcome ChangeLimits(WindowLimitsChanged changed)
    {
        var desk = WorkspaceContaining(changed.Window);
        if (desk is null)
            return View([]);

        // The window's own limits changed. A min_size or max_size rule still wins over them.
        var rule = _sizes.TryGetValue(changed.Window, out var kept) ? kept.Rule : default;
        var tiled = RememberSizes(changed.Window, rule, changed.MinSize, changed.MaxSize);
        var floating = rule.Floating(changed.MinSize, changed.MaxSize);
        var placements = desk.Layout.UpdateLimits(changed.Window, floating.Min, floating.Max, tiled);
        return IsShown(desk.Id) ? View(placements) : View([]);
    }

    // The size rules and the window's own limits of each Client, so its limits can be worked out again when either
    // the window or misc:size_limits_tiled changes.
    private readonly Dictionary<WindowId, (SizeRule Rule, Size? OwnMin, Size? OwnMax)> _sizes = [];

    private TiledLimits RememberSizes(WindowId window, SizeRule rule, Size? ownMin, Size? ownMax)
    {
        _sizes[window] = (rule, ownMin, ownMax);
        return rule.Tiled(ownMin, ownMax, _session.SizeLimitsTiled);
    }

    // misc:size_limits_tiled changed, so every tiled Client keeps other limits. The Layouts are laid out again after.
    private void RefreshTiledLimits()
    {
        foreach (var (window, sizes) in _sizes)
        {
            if (WorkspaceContaining(window) is not LiveWorkspace desk)
                continue;

            var floating = sizes.Rule.Floating(sizes.OwnMin, sizes.OwnMax);
            desk.Layout.UpdateLimits(window, floating.Min, floating.Max, sizes.Rule.Tiled(sizes.OwnMin, sizes.OwnMax, _session.SizeLimitsTiled));
        }
    }

    private Outcome Close(WindowId window)
    {
        _windowState.Forget(window);
        _sizes.Remove(window);
        _initialWorkspaces.Closed(window);
        _shownMaximized.Remove(window);
        if (ForgetSwallowedParent(window))
            return View([]);

        var desk = WorkspaceContaining(window);
        if (desk is null)
        {
            _swallows.Remove(window);
            return View([]);
        }

        var covered = desk.Covered();
        var wasActive = _active == window;
        var mode = desk.ClientOf(window)?.Fullscreen ?? FullscreenMode.None;

        // The parent this window swallowed comes back before the window leaves its Group or its Layout.
        if (GiveSlotBack(window, desk, covered, wasActive) is Outcome slotted)
            return slotted;

        var returned = Unswallow(window, desk);
        if (CloseGroupMember(window, desk, covered, returned, mode) is Outcome grouped)
            return grouped;

        var monitor = desk.Monitor;
        var special = desk.Id.IsSpecial && SpecialOn(monitor) == desk.Id;
        var floating = desk.ClientOf(window)?.Floating == true;
        var hadBounds = desk.Layout.TryBounds(window, out var bounds);
        var placements = Restamp(desk, desk.Layout.Remove(window));
        if (!IsShown(desk.Id))
            placements = [];

        if (special && desk.Layout.Clients.Count == 0)
        {
            if (_session.CloseSpecialOnEmpty)
            {
                SetSpecial(monitor, null);
                DropIfEmpty(desk.Id);
                var normal = FindWorkspace(ActiveOn(monitor));
                var specialFocus = normal is null ? null : normal.Remembered();
                _active = specialFocus;
                if (normal is not null)
                    normal.Focus = specialFocus;
                return View(normal?.Layout.Visible() ?? [], focus: specialFocus);
            }

            _active = _input.SpecialFallthrough ? (FindWorkspace(ActiveOn(monitor)) ?? FocusedWorkspace()).Remembered() : null;
            desk.Focus = null;
            return View([], focus: _active);
        }

        DropIfEmpty(desk.Id);
        if (!IsShown(desk.Id))
        {
            if (wasActive)
                _active = null;

            return View(placements);
        }

        WindowId? next = null;
        var reveal = false;
        if (wasActive)
        {
            next = returned ?? FocusAfter(desk, floating, hadBounds ? bounds : default, hadBounds);
            if (next is WindowId chosen)
            {
                TakeFocus(desk, chosen);
                if (RetainFullscreen(desk, chosen, mode, grouped: chosen == returned) is { } retained)
                    placements = retained;
            }
            else if (_input.FocusOnClose == 0)
                reveal = true;
            else
                TakeFocus(desk, null);
        }

        var outcome = Present(desk, covered, placements, focus: next, focusRevealed: reveal);
        if (wasActive && reveal && _active == window)
        {
            var fallback = desk.LastVisible();
            TakeFocus(desk, fallback);
            outcome = outcome with { Focus = fallback };
        }

        return outcome;
    }

    private Outcome Minimize(WindowId window)
    {
        var desk = WorkspaceContaining(window);
        if (desk is null)
            return View([]);

        var covered = desk.Covered();
        var wasActive = _active == window;
        var placements = desk.Layout.Minimize(window);
        return IsShown(desk.Id)
            ? Present(desk, covered, placements, focusRevealed: wasActive)
            : View([]);
    }

    private Outcome Restore(WindowRestored restored)
    {
        var desk = WorkspaceContaining(restored.Window);
        if (desk is null)
            return View([]);

        var covered = desk.Covered();
        var placements = desk.Layout.Restore(restored.Window);
        if (!IsShown(desk.Id))
            return View([], hide: [restored.Window]);

        return Present(desk, covered, placements);
    }

    private Outcome ChangePseudo(WindowId window)
    {
        var desk = WorkspaceContaining(window);
        if (desk is null)
            return View([]);

        var covered = desk.Covered();
        var placements = desk.Layout.TogglePseudo(window);
        return IsShown(desk.Id) ? Present(desk, covered, placements) : View([]);
    }
}
