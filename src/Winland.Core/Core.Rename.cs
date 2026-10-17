using System.Globalization;

namespace Winland;

public sealed partial class Core
{
    /// <summary>
    /// <c>renameworkspace WORKSPACE NAME</c> gives a Workspace a new display name, as the pinned release's rename
    /// does. Its address stays, so selectors, rules and other dispatchers still find it by the old one. A special
    /// workspace keeps its name.
    /// </summary>
    private Outcome RenameWorkspace(string arguments, bool allowsElevation)
    {
        const string dispatcher = "renameworkspace";
        var text = arguments.Trim();
        var split = text.IndexOfAny([' ', '\t']);
        var selector = split < 0 ? text : text[..split];
        var name = split < 0 ? "" : text[(split + 1)..].Trim();
        if (RenameTarget(selector) is not WorkspaceId id || FindWorkspace(id) is not { } desk)
        {
            return View([], dispatcher: dispatcher, allowsElevation: allowsElevation) with
            {
                Error = "error: no such workspace",
            };
        }

        if (id.IsSpecial)
            return View([], dispatcher: dispatcher, allowsElevation: allowsElevation);

        desk.DisplayName = name;
        desk.Renamed = true;
        (_extraEvents ??= []).Add(new WorkspaceRenamed(id, name));

        // A rule that reads the display name may now match, or stop matching.
        var shown = ActiveOn(desk.Monitor) == id;
        var placements = Restamp(desk, []);
        return View(shown ? placements : [], dispatcher: dispatcher, allowsElevation: allowsElevation);
    }

    // The workspace a rename names, read as the pinned release's workspace query reads it: by number, by address
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
