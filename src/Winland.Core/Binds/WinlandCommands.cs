namespace Winland.Core.Binds;

/// <summary>
/// The chord ids Winland owns in <c>bindings.json</c>. A window command with no payload is a fixed word such as <c>close</c>.
/// The rest are read by their shape: <c>workspace.jump.3</c>, <c>focus.right</c>, <c>resize.left.fine</c>.
/// A shell command, an app role, a panel, and a plugin command are not Winland's, so they do not parse here.
/// </summary>
public static class WinlandCommands
{
    private static readonly Dictionary<string, WinlandCommand> Words = BuildWords();

    /// <summary>The id <paramref name="command"/> is stored as.</summary>
    /// <param name="command">A Winland command.</param>
    /// <returns>Its chord id.</returns>
    public static string IdOf(WinlandCommand command) => command switch
    {
        WinlandCommand.JumpWorkspace jump => "workspace.jump." + jump.Number,
        WinlandCommand.MoveWindow move => (move.Follow ? "workspace.move." : "workspace.send.") + move.Number,
        WinlandCommand.CycleWorkspace cycle => "workspace.cycle." + cycle.Cycle.ToString().ToLowerInvariant(),
        WinlandCommand.Focus focus => "focus." + focus.Direction.ToString().ToLowerInvariant(),
        WinlandCommand.Swap swap => "swap." + swap.Direction.ToString().ToLowerInvariant(),
        WinlandCommand.Resize resize => ResizeId(resize),
        WinlandCommand.GroupStep step => step.Delta < 0 ? "group.prev" : "group.next",
        WinlandCommand.GroupIndex index => "group.show." + index.Index,
        WinlandCommand.JoinGroup join => "group.join." + join.Direction.ToString().ToLowerInvariant(),
        WinlandCommand.MoveMonitor monitor => "monitor." + monitor.Direction.ToString().ToLowerInvariant(),
        WinlandCommand.Window window => WindowId(window.Action),
        WinlandCommand.Nothing => "nothing",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    /// <summary>The command named <paramref name="id"/>, or null when the id is not Winland's. Case is ignored.</summary>
    /// <param name="id">A chord id.</param>
    /// <param name="command">The command, when this returns true.</param>
    /// <returns>True when <paramref name="id"/> names a Winland command.</returns>
    public static bool TryParse(string? id, out WinlandCommand? command)
    {
        command = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var parts = id.Split('.');
        if (parts.Length == 3
            && parts[0].Equals("workspace", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[2], out var number)
            && number is >= 1 and <= 9)
        {
            command = parts[1].ToLowerInvariant() switch
            {
                "jump" => new WinlandCommand.JumpWorkspace(number),
                "move" => new WinlandCommand.MoveWindow(number, Follow: true),
                "send" => new WinlandCommand.MoveWindow(number, Follow: false),
                _ => null,
            };
            return command is not null;
        }

        if (parts.Length == 2)
        {
            command = parts[0].ToLowerInvariant() switch
            {
                "workspace" when parts[1].Equals("cycle", StringComparison.OrdinalIgnoreCase) => null,
                "focus" when TryDirection(parts[1], out var focus) => new WinlandCommand.Focus(focus),
                "swap" when TryDirection(parts[1], out var swap) => new WinlandCommand.Swap(swap),
                "resize" when TryResize(parts[1], 100, out var resize) => resize,
                "group" when parts[1].Equals("next", StringComparison.OrdinalIgnoreCase) => new WinlandCommand.GroupStep(1),
                "group" when parts[1].Equals("prev", StringComparison.OrdinalIgnoreCase) => new WinlandCommand.GroupStep(-1),
                _ => null,
            };
            if (command is not null)
            {
                return true;
            }

            if (parts[0].Equals("workspace", StringComparison.OrdinalIgnoreCase)
                && parts[1].Equals("cycle", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (parts.Length == 3 && parts[0].Equals("resize", StringComparison.OrdinalIgnoreCase))
        {
            var pixels = Step(parts[2]);
            if (pixels > 0 && TryResize(parts[1], pixels, out var stepped))
            {
                command = stepped;
                return true;
            }
        }

        if (parts.Length == 3
            && parts[0].Equals("group", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("show", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[2], out var member)
            && member is >= 1 and <= 5)
        {
            command = new WinlandCommand.GroupIndex(member);
            return true;
        }

        if (parts.Length == 2
            && parts[0].Equals("monitor", StringComparison.OrdinalIgnoreCase)
            && TryDirection(parts[1], out var monitor))
        {
            command = new WinlandCommand.MoveMonitor(monitor);
            return true;
        }

        if (parts.Length == 3
            && parts[0].Equals("group", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("join", StringComparison.OrdinalIgnoreCase)
            && TryDirection(parts[2], out var join))
        {
            command = new WinlandCommand.JoinGroup(join);
            return true;
        }

        if (parts is ["workspace", "cycle", _] && TryName<WorkspaceCycle>(parts[2], out var cycle))
        {
            command = new WinlandCommand.CycleWorkspace(cycle);
            return true;
        }

        if (Words.TryGetValue(id, out var word))
        {
            command = word;
            return true;
        }

        return false;
    }

    private static Dictionary<string, WinlandCommand> BuildWords()
    {
        var map = new Dictionary<string, WinlandCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in Enum.GetValues<WindowAction>())
        {
            map.Add(WindowId(action), new WinlandCommand.Window(action));
        }

        map.Add("nothing", new WinlandCommand.Nothing());
        return map;
    }

    // CS8524 is the unnamed-cast case. A new WindowAction still fails the build until it has a case.
#pragma warning disable CS8524
    private static string WindowId(WindowAction action) => action switch
    {
        WindowAction.Close => "close",
        WindowAction.ToggleFloat => "float",
        WindowAction.ToggleFullscreen => "fullscreen",
        WindowAction.ToggleTiledFullscreen => "tiled-fullscreen",
        WindowAction.ToggleFullWidth => "full-width",
        WindowAction.ToggleLayout => "layout",
        WindowAction.ToggleSplit => "split",
        WindowAction.TogglePseudo => "pseudo",
        WindowAction.ToggleSticky => "sticky",
        WindowAction.ToggleSpacing => "spacing",
        WindowAction.ToggleSquareTile => "square-tile",
        WindowAction.ToggleFullscreenDesktop => "fullscreen-desktop",
        WindowAction.ToggleGroup => "group",
        WindowAction.LeaveGroup => "leave-group",
        WindowAction.ToggleScratchpad => "scratchpad",
        WindowAction.SendScratchpad => "scratchpad-send",
        WindowAction.NextMonitor => "next-monitor",
        WindowAction.PreviousMonitor => "previous-monitor",
        WindowAction.SaveWidth => "save-width",
        WindowAction.RestoreWidth => "restore-width",
    };
#pragma warning restore CS8524

    private static bool TryDirection(string text, out Direction direction) =>
        TryName(text, out direction);

    // Enum.TryParse reads a number as the value it stands for and a comma as several names joined into one value,
    // so "focus.7" would bind a direction that does not exist and "focus.right,up" would bind down, and neither would say so.
    private static bool TryName<T>(string text, out T value)
        where T : struct, Enum
    {
        value = default;
        return text.Length > 0
            && char.IsAsciiLetter(text[0])
            && !text.Contains(',')
            && Enum.TryParse(text, ignoreCase: true, out value);
    }

    private static string ResizeId(WinlandCommand.Resize resize)
    {
        var (name, pixels) = resize switch
        {
            { Y: 0, X: < 0 } => ("left", -resize.X),
            { Y: 0, X: > 0 } => ("right", resize.X),
            { X: 0, Y: < 0 } => ("up", -resize.Y),
            { X: 0, Y: > 0 } => ("down", resize.Y),
            _ => ("left", 100),
        };
        var step = pixels switch
        {
            25 => ".fine",
            300 => ".coarse",
            _ => "",
        };
        return "resize." + name + step;
    }

    private static int Step(string name) => name.ToLowerInvariant() switch
    {
        "fine" => 25,
        "coarse" => 300,
        _ => 0,
    };

    private static bool TryResize(string edge, int pixels, out WinlandCommand resize)
    {
        resize = edge.ToLowerInvariant() switch
        {
            "left" => new WinlandCommand.Resize(-pixels, 0),
            "right" => new WinlandCommand.Resize(pixels, 0),
            "up" => new WinlandCommand.Resize(0, -pixels),
            "down" => new WinlandCommand.Resize(0, pixels),
            _ => null!,
        };
        return edge.Equals("left", StringComparison.OrdinalIgnoreCase)
            || edge.Equals("right", StringComparison.OrdinalIgnoreCase)
            || edge.Equals("up", StringComparison.OrdinalIgnoreCase)
            || edge.Equals("down", StringComparison.OrdinalIgnoreCase);
    }
}
