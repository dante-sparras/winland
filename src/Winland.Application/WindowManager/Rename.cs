using System.Globalization;

namespace Winland.Application;

/// <summary>The <c>changeworkspaceid</c> dispatcher.</summary>
public sealed partial class WindowManager
{
    /// <summary>
    /// <c>changeworkspaceid WORKSPACE ID</c> gives a numbered Workspace on the focused Monitor another number
    /// that Monitor does not have. Its Clients and the Workspace history follow it, and the rules for the new
    /// number apply. A Guest keeps the number it had on its own Monitor.
    /// </summary>
    private Outcome ChangeWorkspaceId(string arguments)
    {
        const string dispatcher = "changeworkspaceid";
        var parts = arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number <= 0
            || number > WorkspaceId.MaxNumber
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var current)
            || current <= 0
            || current > WorkspaceId.MaxNumber)
        {
            return View([], dispatcher: dispatcher) with
            {
                Error = parts.Length == 2 && IsDigits(parts[0]) ? "error: bad workspace" : "error: invalid workspace id",
            };
        }

        var monitor = FocusedId();
        var from = WorkspaceId.FromNumber(current, monitor);
        var to = WorkspaceId.FromNumber(number, monitor);
        if (!from.TryGetNumber(out var old)
            || _workspaces.Find(from) is not { } desk
            || desk.Id.IsSpecial
            || (_workspaces.Find(to) is { } occupied && occupied != desk))
        {
            return View([], dispatcher: dispatcher) with { Error = "error: bad workspace" };
        }

        Rebind(desk, to);
        var screen = MonitorById(desk.Monitor);
        (_extraEvents ??= []).Add(new WorkspaceIdChanged(old, number, screen?.Reported.Name ?? ""));

        var shown = ActiveOn(desk.Monitor) == to;
        var placements = Restamp(desk, []);
        return View(shown ? placements : [], dispatcher: dispatcher);
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}
