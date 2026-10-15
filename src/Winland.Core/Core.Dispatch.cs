using static Winland.OrdinalText;

namespace Winland;

public sealed partial class Core
{
    private Outcome RunBind(BindDecision match)
    {
        var dispatches = DispatchesOf(match);
        Outcome? outcome = null;
        foreach (var dispatch in dispatches)
        {
            if (dispatch.Dispatcher.Length == 0)
                continue;

            var submapBefore = _keyState.Submap;
            var next = ExecuteTriggered(dispatch.Dispatcher, dispatch.Arguments, match.Trigger);
            outcome = outcome is null ? next : Merge(outcome, next);
            if (!Is(dispatch.Dispatcher, "submap")
                && dispatch.SubmapReset is string reset
                && reset.Length > 0
                && _keyState.Submap == submapBefore)
                TryEnterSubmap(reset, out _);
        }

        if (outcome is null)
            return View([], consumed: match.Consumed);

        return outcome with { Consumed = match.Consumed, Dispatcher = match.Dispatcher ?? outcome.Dispatcher };
    }

    private static IReadOnlyList<BindDispatch> DispatchesOf(BindDecision match)
    {
        if (match.Dispatches.Count > 0)
            return match.Dispatches;
        if (string.IsNullOrEmpty(match.Dispatcher))
            return [];

        return [new BindDispatch(match.Dispatcher, match.Arguments)];
    }

    private static Outcome Merge(Outcome first, Outcome second) =>
        second with
        {
            Placements = second.Placements.Count > 0 ? second.Placements : first.Placements,
            Close = second.Close ?? first.Close,
            Focus = second.Focus ?? first.Focus,
            Launch = second.Launch ?? first.Launch,
            Kill = second.Kill ?? first.Kill,
            Exit = second.Exit || first.Exit,
            Notice = second.Notice ?? first.Notice,
            DisplayPower = second.DisplayPower.Count > 0 ? second.DisplayPower : first.DisplayPower,
            Relays = [.. first.Relays, .. second.Relays],
            ZOrder = [.. first.ZOrder, .. second.ZOrder],
            Show = [.. first.Show, .. second.Show],
            Hide = [.. first.Hide, .. second.Hide],
            ShowMinimized = [.. first.ShowMinimized, .. second.ShowMinimized],
        };

    private Outcome ExecuteTriggered(string name, string arguments, BindTrigger? trigger)
    {
        _trigger = trigger;
        try
        {
            return Execute(name, arguments, allowsElevation: true);
        }
        finally
        {
            _trigger = null;
        }
    }

    private Outcome Execute(string name, string arguments, bool allowsElevation)
    {
        ArgumentNullException.ThrowIfNull(name);
        arguments ??= "";

        if (Is(name, "togglespecialworkspace"))
            return ToggleSpecial(arguments, allowsElevation);

        if (GroupDispatch(name, arguments, allowsElevation) is Outcome grouped)
            return grouped;

        if (Is(name, "workspace") || Is(name, "focusworkspaceoncurrentmonitor"))
        {
            if (!WorkspaceAddress.TryResolve(arguments, Context(), out var target))
                return View([], dispatcher: name, allowsElevation: allowsElevation);

            if (target.IsSpecial)
                return OpenSpecial(target, null, name, allowsElevation);

            return SwitchTo(
                Alias(target),
                focus: null,
                consumed: false,
                dispatcher: name,
                allowsElevation: allowsElevation,
                pull: Is(name, "focusworkspaceoncurrentmonitor"));
        }

        if (Is(name, "movetoworkspace"))
            return MoveWindowToWorkspace(arguments, silent: false, allowsElevation: allowsElevation);

        if (Is(name, "movetoworkspacesilent"))
            return MoveWindowToWorkspace(arguments, silent: true, allowsElevation: allowsElevation);

        if (Is(name, "movefocus"))
            return MoveFocusDirection(arguments, allowsElevation);

        if (Is(name, "movewindow"))
            return MoveWindowDirection(arguments, allowsElevation);

        if (Is(name, "focusmonitor"))
            return FocusMonitorDispatch(arguments, allowsElevation);

        if (Is(name, "movecurrentworkspacetomonitor"))
            return MoveCurrentWorkspace(arguments, allowsElevation);

        if (Is(name, "moveworkspacetomonitor"))
            return MoveWorkspace(arguments, allowsElevation);

        if (Is(name, "swapactiveworkspaces"))
            return SwapActiveWorkspaces(arguments, allowsElevation);

        var active = ActiveWindow();
        var desk = CurrentWorkspace();
        var layout = desk.Layout;
        var covered = desk.Covered();
        IReadOnlyList<Placement> placements = [];
        WindowId? close = null;
        WindowId? focus = null;

        if (Is(name, "killactive"))
        {
            if (active is WindowId closing && !Closeable(closing))
                return Rejected(name, NotCloseable, allowsElevation);

            close = active;
        }
        else if (Is(name, "resizeactive"))
            placements = active is WindowId window ? layout.ResizeActive(window, arguments) : [];
        else if (Is(name, "togglefloating"))
            return FloatWindow(arguments, floating: null, allowsElevation);
        else if (Is(name, "setfloating"))
            return FloatWindow(arguments, floating: true, allowsElevation);
        else if (Is(name, "settiled"))
            return FloatWindow(arguments, floating: false, allowsElevation);
        else if (Is(name, "pin"))
            return PinWindow(arguments, allowsElevation);
        else if (Is(name, "pseudo"))
            return PseudoWindow(arguments, allowsElevation);
        else if (Is(name, "focuswindow"))
            return FocusWindow(arguments, allowsElevation);
        else if (Is(name, "closewindow"))
            return CloseWindow(arguments, allowsElevation);
        else if (Is(name, "killwindow"))
            return KillWindow(arguments, allowsElevation);
        else if (Is(name, "swapwindow"))
            return SwapWindow(arguments, allowsElevation);
        else if (Is(name, "movewindowpixel"))
            return MoveWindowPixel(arguments, allowsElevation);
        else if (Is(name, "resizewindowpixel"))
            return ResizeWindowPixel(arguments, allowsElevation);
        else if (Is(name, "centerwindow"))
            return CenterWindow(allowsElevation);
        else if (Is(name, "alterzorder") || Is(name, "bringactivetotop"))
            return AlterZOrder(arguments, Is(name, "bringactivetotop"), allowsElevation);
        else if (Is(name, "movecursortocorner"))
            return MoveCursorToCorner(arguments, allowsElevation);
        else if (Is(name, "movetoroot"))
            return MoveRoot(arguments, allowsElevation);
        else if (Is(name, "exit"))
            return ExitDaemon(allowsElevation);
        else if (Is(name, "event"))
            return EmitCustom(arguments, allowsElevation);
        else if (Is(name, "dpms"))
            return Dpms(arguments, allowsElevation);
        else if (Is(name, "pass"))
            return Pass(arguments, allowsElevation);
        else if (Is(name, "sendshortcut"))
            return SendShortcut(arguments, allowsElevation);
        else if (Is(name, "sendkeystate"))
            return SendKeyState(arguments, allowsElevation);
        else if (Is(name, "focusurgentorlast"))
            return FocusUrgentOrLast(allowsElevation);
        else if (Is(name, "toggleswallow"))
            return ToggleSwallow(allowsElevation);
        else if (Is(name, "fullscreen") || Is(name, "fullscreenstate") || Is(name, "maximize"))
            return FullscreenDispatcher(name, arguments, allowsElevation);
        else if (Is(name, "layoutmsg") || Is(name, "layout"))
            placements = layout.LayoutMessage(active, arguments);
        else if (DwindleMessages.Contains(name))
            placements = layout.LayoutMessage(active, Join(name, arguments));
        else if (Is(name, "exec") || Is(name, "execr"))
        {
            var parsed = LaunchParser.Parse(arguments, allowsElevation, raw: Is(name, "execr"));
            if (parsed.Warnings.Count > 0)
            {
                var warnings = new List<StreamEvent>(parsed.Warnings.Count);
                foreach (var warning in parsed.Warnings)
                    warnings.Add(new ConfigWarned(0, warning, null));
                _configEvents = warnings;
            }

            return View([], dispatcher: name, allowsElevation: allowsElevation) with
            {
                Launch = WithEnvironment(parsed.Launch),
            };
        }
        else if (Is(name, "submap"))
        {
            if (!TryEnterSubmap(arguments, out var error))
                return View([], dispatcher: name, allowsElevation: allowsElevation) with { Error = error };

            return View([], dispatcher: name, allowsElevation: allowsElevation);
        }
        else if (Dispatchers.TryInvoke(name, arguments, out var pluginResult))
            return View([], dispatcher: name, allowsElevation: allowsElevation) with
            {
                Error = pluginResult.Error,
                Notice = pluginResult.Notice,
            };
        else if (!Dispatchers.IsKnown(name))
            return View([], dispatcher: name, allowsElevation: allowsElevation) with
            {
                Error = "error: unknown dispatcher '" + name + "'",
            };
        else
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (layout.LayoutMessageError is string layoutError
            && (Is(name, "layoutmsg") || Is(name, "layout") || DwindleMessages.Contains(name)))
            return View(placements, dispatcher: name, allowsElevation: allowsElevation) with { Error = layoutError };

        if (focus is null && layout.LayoutMessageFocus is WindowId requested)
            focus = requested;

        if (focus is WindowId next && HasNoFocus(next))
            focus = null;
        else if (focus is WindowId accepted)
        {
            _active = accepted;
            desk.Focus = accepted;
            desk.Layout.NoteFocus(accepted);
        }

        var message = Is(name, "layoutmsg") || Is(name, "layout") || DwindleMessages.Contains(name);
        return Present(
            desk,
            covered,
            placements,
            dispatcher: name,
            close: close,
            focus: focus,
            allowsElevation: allowsElevation,
            focusRevealed: message && focus is null);
    }

    private static string Join(string name, string arguments) =>
        arguments.Length == 0 ? name : string.Concat(name, " ", arguments);

    private bool TryEnterSubmap(string arguments, out string? error)
    {
        var name = arguments.Trim();
        if (name.Length == 0 || name == "reset")
        {
            EnterSubmap("");
            error = null;
            return true;
        }

        if (!_binds.HasSubmap(name))
        {
            error = "error: submap '" + name + "' does not exist";
            return false;
        }

        EnterSubmap(name);
        error = null;
        return true;
    }

    private void EnterSubmap(string name)
    {
        _keyState.Submap = name;
        _noted ??= [];
        _noted.Add(new SubmapChanged(name));
    }
}
