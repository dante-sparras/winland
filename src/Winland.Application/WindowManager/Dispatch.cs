using static Winland.Domain.OrdinalText;

namespace Winland.Application;

public sealed partial class WindowManager
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
            Placements = Union(first.Placements, second.Placements, second.State),
            Closes = [.. first.Closes, .. second.Closes],
            Focus = second.Focus ?? first.Focus,
            Launches = [.. first.Launches, .. second.Launches],
            Kills = [.. first.Kills, .. second.Kills],
            Exit = second.Exit || first.Exit,
            DisplayPower = [.. first.DisplayPower, .. second.DisplayPower],
            Relays = [.. first.Relays, .. second.Relays],
            ZOrder = [.. first.ZOrder, .. second.ZOrder],
            Show = [.. first.Show, .. second.Show],
            Hide = [.. first.Hide, .. second.Hide],
            ShowMinimized = [.. first.ShowMinimized, .. second.ShowMinimized],
        };

    // Every placement of the later outcome, and each earlier one it does not replace, for a Client that is still
    // open and not hidden. A command that moves one floating window places only that window, so the earlier
    // command's window would otherwise keep its old rectangle.
    private static IReadOnlyList<Placement> Union(IReadOnlyList<Placement> first, IReadOnlyList<Placement> second, State state)
    {
        if (first.Count == 0)
            return second;

        var placed = new HashSet<ClientId>(second.Count);
        foreach (var placement in second)
            placed.Add(placement.Client);

        var open = new HashSet<ClientId>(state.Clients.Count);
        foreach (var client in state.Clients)
            open.Add(client.Id);
        foreach (var client in state.Hidden)
            open.Remove(client.Id);

        var merged = new List<Placement>(first.Count + second.Count);
        foreach (var placement in first)
        {
            if (!placed.Contains(placement.Client) && open.Contains(placement.Client))
                merged.Add(placement);
        }

        merged.AddRange(second);
        return merged;
    }

    private Outcome ExecuteTriggered(string name, string arguments, BindTrigger? trigger)
    {
        _trigger = trigger;
        try
        {
            return Execute(name, arguments);
        }
        finally
        {
            _trigger = null;
        }
    }

    // Runs a dispatcher from the table. One that takes a window and is given one after a comma runs on that window.
    private Outcome Execute(string name, string arguments)
    {
        ArgumentNullException.ThrowIfNull(name);
        arguments ??= "";

        if (!Table.TryGetValue(name, out var dispatcher))
            return View([], dispatcher: name) with { Error = "error: unknown dispatcher '" + name + "'" };

        if (dispatcher.TakesWindow && arguments.IndexOf(',', StringComparison.Ordinal) is var comma and >= 0)
            return ExecuteOn(name, arguments[..comma], arguments[(comma + 1)..].Trim());

        return dispatcher.Run(this, name, arguments);
    }

    private Outcome WorkspaceDispatch(string name, string arguments)
    {
        if (!WorkspaceAddress.TryResolve(arguments, Context(), out var target))
            return View([], dispatcher: name) with { Error = WorkspaceAddress.IsNamed(arguments) ? "error: no such workspace" : null };

        if (target.IsSpecial)
            return OpenSpecial(target, null, name);

        var from = FocusedId();
        var switched = SwitchTo(BackAndForth(target), focus: null, consumed: false, dispatcher: name);
        NoteCrossMonitorWarp(from, switched);
        return switched;
    }

    private Outcome Exec(string name, string arguments)
    {
        var parsed = LaunchParser.Parse(arguments, raw: Is(name, "execr"));
        if (parsed.Warnings.Count > 0)
        {
            var warnings = new List<StreamEvent>(parsed.Warnings.Count);
            foreach (var warning in parsed.Warnings)
                warnings.Add(new ConfigWarned(0, warning, null));
            _configEvents = warnings;
        }

        var launched = View([], dispatcher: name);
        return parsed.Launch is Launch launch
            ? launched with { Launches = [.. launched.Launches, launch with { InitialWorkspace = LaunchWorkspace() }] }
            : launched;
    }

    private Outcome SubmapDispatch(string name, string arguments) =>
        TryEnterSubmap(arguments, out var error)
            ? View([], dispatcher: name)
            : View([], dispatcher: name) with { Error = error };

    // The dispatchers the active Workspace's Layout answers: killactive, resizeactive, and the layout messages.
    private Outcome LayoutDispatch(string name, string arguments)
    {
        var active = ActiveWindow();
        var desk = CurrentWorkspace();
        var layout = desk.Layout;
        var covered = desk.Covered();
        IReadOnlyList<Placement> placements = [];
        WindowId? close = null;
        WindowId? focus = null;
        var message = false;

        if (Is(name, "killactive"))
        {
            if (active is WindowId closing && !Closeable(closing))
                return Rejected(name, NotCloseable);

            close = active;
        }
        else if (Is(name, "resizeactive"))
            placements = active is WindowId window ? layout.ResizeActive(window, arguments) : [];
        else
        {
            message = true;
            placements = Is(name, "layoutmsg") || Is(name, "layout")
                ? layout.LayoutMessage(active, arguments)
                : layout.LayoutMessage(active, Join(name, arguments));
        }

        if (message && layout.LayoutMessageError is string layoutError)
            return View(placements, dispatcher: name) with { Error = layoutError };

        if (layout.LayoutMessageFocus is WindowId requested)
            focus = requested;

        if (focus is WindowId next && HasNoFocus(next))
            focus = null;
        else if (focus is WindowId accepted)
        {
            _active = accepted;
            desk.Focus = accepted;
            desk.Layout.NoteFocus(accepted);
        }

        return Present(
            desk,
            covered,
            placements,
            dispatcher: name,
            close: close,
            focus: focus,
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

        if (!_config.Binds.HasSubmap(name))
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
