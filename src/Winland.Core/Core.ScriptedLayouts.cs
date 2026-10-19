using System.Globalization;

namespace Winland;

// The Layouts a config's script arranges (ADR 0013): the core asks the script through LayoutScripts, inside its lock,
// and the answer comes back later as layoutplace on the pipe, which moves the tiles of that Layout's Workspace.
public sealed partial class Core
{
    /// <summary>
    /// Arranges the Workspaces whose Layout is a registered <c>lua:NAME</c>. Without it such a Layout tiles as an even
    /// grid. Set it before the config is applied.
    /// </summary>
    public ILayoutScripts? LayoutScripts { get; set; }

    /// <summary>
    /// <c>layoutplace LAYOUT GENERATION [failed | WINDOW X Y W H ...]</c>: a script's answer to a request
    /// (<see cref="LayoutRequest.Answer"/>). An answer to a Layout that is gone, or to a request a newer one replaced,
    /// changes nothing.
    /// </summary>
    private CommandReply PlaceScripted(string arguments)
    {
        if (!TryReadPlacement(arguments, out var layout, out var generation, out var placed))
            return new CommandReply("error: invalid layout placement", View([]));

        foreach (var desk in _workspaces)
        {
            if (desk.Layout is not ScriptedLayout scripted || scripted.Id != layout)
                continue;

            return scripted.Accept(generation, placed)
                ? new CommandReply("ok", View(ShownPlacements()))
                : new CommandReply("ok", View([]));
        }

        return new CommandReply("ok", View([]));
    }

    private static bool TryReadPlacement(
        string arguments,
        out long layout,
        out long generation,
        out IReadOnlyList<(WindowId Window, LayoutBox Box)>? placed)
    {
        placed = null;
        generation = 0;
        var words = arguments.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2
            || !long.TryParse(words[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out layout)
            || !long.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out generation))
        {
            layout = 0;
            return false;
        }

        if (words.Length == 3 && words[2] == "failed")
            return true;

        if ((words.Length - 2) % 5 != 0)
            return false;

        var boxes = new List<(WindowId, LayoutBox)>((words.Length - 2) / 5);
        for (var i = 2; i < words.Length; i += 5)
        {
            var address = words[i];
            if (!address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                || !long.TryParse(address[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var window))
                return false;

            var values = new double[4];
            for (var j = 0; j < 4; j++)
            {
                if (!double.TryParse(words[i + 1 + j], NumberStyles.Float, CultureInfo.InvariantCulture, out values[j]) || !double.IsFinite(values[j]))
                    return false;
            }

            boxes.Add((new WindowId(window), new LayoutBox(values[0], values[1], values[2], values[3])));
        }

        placed = boxes;
        return true;
    }
}
