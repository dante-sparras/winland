using System.Globalization;

namespace Winland;

public sealed partial class Core
{
    /// <summary>
    /// <c>renameworkspace WORKSPACE NAME</c> gives a Workspace a new display name, as Winland's rename
    /// does. Its address stays, so selectors, rules and other dispatchers still find it by the old one. A special
    /// workspace keeps its name.
    /// </summary>
    private Outcome RenameWorkspace(string arguments)
    {
        const string dispatcher = "renameworkspace";
        var text = arguments.Trim();
        var split = text.IndexOfAny([' ', '\t']);
        var selector = split < 0 ? text : text[..split];
        var name = split < 0 ? "" : text[(split + 1)..].Trim();
        if (RenameTarget(selector) is not WorkspaceId id || FindWorkspace(id) is not { } desk)
        {
            return View([], dispatcher: dispatcher) with
            {
                Error = "error: no such workspace",
            };
        }

        if (id.IsSpecial)
            return View([], dispatcher: dispatcher);

        desk.DisplayName = name;
        desk.Renamed = true;
        (_extraEvents ??= []).Add(new WorkspaceRenamed(id, name));

        // A rule that reads the display name may now match, or stop matching.
        var shown = ActiveOn(desk.Monitor) == id;
        var placements = Restamp(desk, []);
        return View(shown ? placements : [], dispatcher: dispatcher);
    }

    /// <summary>
    /// <c>changeworkspaceid WORKSPACE ID</c> gives a numbered Workspace another number no Workspace has, as the pinned
    /// release's id change does. Its Clients, the Monitor showing it and the Workspace history follow it, and the rules
    /// for the new number apply. A Workspace that was not renamed takes the new number as its name.
    /// </summary>
    private Outcome ChangeWorkspaceId(string arguments)
    {
        const string dispatcher = "changeworkspaceid";
        var parts = arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            || number <= 0
            || number > WorkspaceId.MaxNumber)
        {
            return View([], dispatcher: dispatcher) with { Error = "error: invalid workspace id" };
        }

        var to = WorkspaceId.FromNumber(number);
        if (RenameTarget(parts[0]) is not WorkspaceId from
            || !from.TryGetNumber(out var old)
            || FindWorkspace(from) is not { } desk
            || FindWorkspace(to) is not null)
        {
            return View([], dispatcher: dispatcher) with { Error = "error: bad workspace" };
        }

        desk.Readdress(to);
        if (!desk.Renamed)
            desk.DisplayName = null;

        foreach (var screen in _monitors)
        {
            if (screen.Active == from)
                screen.Active = to;
        }

        foreach (var (monitor, active) in _activeOn.ToArray())
        {
            if (active == from)
                _activeOn[monitor] = to;
        }

        foreach (var (monitor, active) in _rememberedActive.ToArray())
        {
            if (active == from)
                _rememberedActive[monitor] = to;
        }

        if (_returnMonitors.Remove(from, out var returning))
            _returnMonitors[to] = returning;

        for (var i = 0; i < _history.Count; i++)
        {
            if (!_history[i].Gone && _history[i].Workspace == from)
                _history[i] = _history[i] with { Workspace = to };
        }

        _initialWorkspaces.Readdress(from, to);
        (_readdressed ??= []).Add((from, to));
        (_extraEvents ??= []).Add(new WorkspaceIdChanged(old, number));

        var shown = ActiveOn(desk.Monitor) == to;
        var placements = Restamp(desk, []);
        return View(shown ? placements : [], dispatcher: dispatcher);
    }

    // The workspace a rename names, read as Winland's workspace query reads it: by number, by address
    // after name: or special, or else by a bare address.
    private static WorkspaceId? RenameTarget(string selector)
    {
        if (selector.StartsWith("name:", StringComparison.Ordinal))
        {
            var address = selector[5..];
            return address.Length == 0 || IsDigits(address) ? null : new WorkspaceId(address);
        }

        if (selector == "special")
            return WorkspaceId.DefaultSpecial;

        if (selector.StartsWith("special:", StringComparison.Ordinal))
            return selector.Length == 8 ? null : new WorkspaceId(selector);

        if (IsDigits(selector))
        {
            return int.TryParse(selector, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
                ? WorkspaceId.FromNumber(number)
                : null;
        }

        return selector.Length == 0 ? null : new WorkspaceId(selector);
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
