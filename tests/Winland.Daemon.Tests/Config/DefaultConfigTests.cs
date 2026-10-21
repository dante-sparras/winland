
namespace Winland.Daemon.Tests;

public class DefaultConfigTests
{
    private const string Binds = """
        SUPER|W|killactive||
        SUPER|Q|killactive||
        SUPER|J|togglesplit||
        SUPER|P|pseudo||
        SUPER|T|togglefloating||
        SUPER|F|fullscreen||
        SUPER ALT|F|fullscreen|1|
        SUPER|O|togglefloating||
        SUPER|O|pin||
        SUPER|K|cyclelayout||
        SUPER ALT|K|cyclelayout|back|
        SUPER|Left|movefocus|l|
        SUPER|Right|movefocus|r|
        SUPER|Up|movefocus|u|
        SUPER|Down|movefocus|d|
        SUPER SHIFT|Left|swapwindow|l|
        SUPER SHIFT|Right|swapwindow|r|
        SUPER SHIFT|Up|swapwindow|u|
        SUPER SHIFT|Down|swapwindow|d|
        SUPER CTRL SHIFT|Left|movewindow|mon:l|
        SUPER CTRL SHIFT|Right|movewindow|mon:r|
        SUPER CTRL SHIFT|Up|movewindow|mon:u|
        SUPER CTRL SHIFT|Down|movewindow|mon:d|
        SUPER ALT SHIFT|Left|movecurrentworkspacetomonitor|l|
        SUPER ALT SHIFT|Right|movecurrentworkspacetomonitor|r|
        SUPER ALT SHIFT|Up|movecurrentworkspacetomonitor|u|
        SUPER ALT SHIFT|Down|movecurrentworkspacetomonitor|d|
        SUPER|1|workspace|1|
        SUPER|2|workspace|2|
        SUPER|3|workspace|3|
        SUPER|4|workspace|4|
        SUPER|5|workspace|5|
        SUPER|6|workspace|6|
        SUPER|7|workspace|7|
        SUPER|8|workspace|8|
        SUPER|9|workspace|9|
        SUPER|0|workspace|10|
        SUPER SHIFT|1|movetoworkspace|1|
        SUPER SHIFT|2|movetoworkspace|2|
        SUPER SHIFT|3|movetoworkspace|3|
        SUPER SHIFT|4|movetoworkspace|4|
        SUPER SHIFT|5|movetoworkspace|5|
        SUPER SHIFT|6|movetoworkspace|6|
        SUPER SHIFT|7|movetoworkspace|7|
        SUPER SHIFT|8|movetoworkspace|8|
        SUPER SHIFT|9|movetoworkspace|9|
        SUPER SHIFT|0|movetoworkspace|10|
        SUPER ALT SHIFT|1|movetoworkspacesilent|1|
        SUPER ALT SHIFT|2|movetoworkspacesilent|2|
        SUPER ALT SHIFT|3|movetoworkspacesilent|3|
        SUPER ALT SHIFT|4|movetoworkspacesilent|4|
        SUPER ALT SHIFT|5|movetoworkspacesilent|5|
        SUPER ALT SHIFT|6|movetoworkspacesilent|6|
        SUPER ALT SHIFT|7|movetoworkspacesilent|7|
        SUPER ALT SHIFT|8|movetoworkspacesilent|8|
        SUPER ALT SHIFT|9|movetoworkspacesilent|9|
        SUPER ALT SHIFT|0|movetoworkspacesilent|10|
        SUPER|S|togglespecialworkspace|scratchpad|
        SUPER|grave|togglespecialworkspace|scratchpad|
        SUPER ALT|S|movetoworkspacesilent|special:scratchpad|
        SUPER SHIFT|grave|movetoworkspacesilent|special:scratchpad|
        SUPER|Tab|workspace|m+1|
        SUPER SHIFT|Tab|workspace|m-1|
        SUPER CTRL|Tab|workspace|previous_per_monitor|
        ALT CTRL|Tab|focusmonitor|+1|
        ALT CTRL SHIFT|Tab|focusmonitor|-1|
        SUPER|minus|resizeactive|-100 0|
        SUPER|equal|resizeactive|100 0|
        SUPER SHIFT|minus|resizeactive|0 -100|
        SUPER SHIFT|equal|resizeactive|0 100|
        SUPER ALT|minus|resizeactive|-25 0|
        SUPER ALT|equal|resizeactive|25 0|
        SUPER CTRL|minus|resizeactive|-300 0|
        SUPER CTRL|equal|resizeactive|300 0|
        SUPER ALT SHIFT|minus|resizeactive|0 -25|
        SUPER ALT SHIFT|equal|resizeactive|0 25|
        SUPER CTRL SHIFT|minus|resizeactive|0 -300|
        SUPER CTRL SHIFT|equal|resizeactive|0 300|
        SUPER|mouse_down|workspace|m+1|
        SUPER|mouse_up|workspace|m-1|
        SUPER|mouse:272|movewindow||Mouse
        SUPER|mouse:273|resizewindow||Mouse
        SUPER|G|togglegroup||
        SUPER ALT|G|moveoutofgroup||
        SUPER ALT|Left|moveintogroup|l|
        SUPER ALT|Right|moveintogroup|r|
        SUPER ALT|Up|moveintogroup|u|
        SUPER ALT|Down|moveintogroup|d|
        SUPER ALT|Tab|changegroupactive|f|
        SUPER ALT SHIFT|Tab|changegroupactive|b|
        SUPER CTRL|Left|changegroupactive|b|
        SUPER CTRL|Right|changegroupactive|f|
        SUPER ALT|mouse_down|changegroupactive|f|
        SUPER ALT|mouse_up|changegroupactive|b|
        SUPER ALT|1|changegroupactive|1|
        SUPER ALT|2|changegroupactive|2|
        SUPER ALT|3|changegroupactive|3|
        SUPER ALT|4|changegroupactive|4|
        SUPER ALT|5|changegroupactive|5|
        SUPER ALT CTRL|W|closeall||
        SUPER|C|sendshortcut|CTRL, C|
        SUPER|V|sendshortcut|CTRL, V|
        SUPER|X|sendshortcut|CTRL, X|
        SUPER|A|sendshortcut|CTRL, A|
        SUPER CTRL|V|sendshortcut|SUPER, V|
        SUPER|Return|exec|wt|
        SUPER SHIFT|Return|exec|start https://|
        SUPER SHIFT|B|exec|start https://|
        SUPER SHIFT|F|exec|explorer|
        SUPER SHIFT|N|exec|notepad|
        """;

    [Fact]
    public void TheEmbeddedDefaultParsesWithNoWarnings()
    {
        var document = ConfigParser.Parse(DefaultConfig.Text);

        Assert.True(
            document.Warnings.Count == 0,
            string.Join('\n', document.Warnings.Select(warning => $"{warning.Line}: {warning.Message}")));
        Assert.Equal(Lines(Binds), document.Binds.Select(Describe));
        var gesture = Assert.Single(document.Gestures);
        Assert.Equal(3, gesture.Fingers);
        Assert.True(gesture.IsWorkspace);
    }

    [Fact]
    public void TheDefaultIsWrittenOnlyWhenTheFileIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "winland-default-config-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "winland", "winland.conf");
        try
        {
            Assert.True(DefaultConfig.WriteIfMissing(path));
            Assert.Equal(DefaultConfig.Text, File.ReadAllText(path));

            File.WriteAllText(path, "bind = SUPER, Q, killactive\n");
            Assert.False(DefaultConfig.WriteIfMissing(path));
            Assert.Equal("bind = SUPER, Q, killactive\n", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Describe(BindDirective directive)
    {
        var bind = Assert.IsType<Bind>(directive);
        Assert.True(Dispatchers.IsKnown(bind.Dispatcher), bind.Dispatcher);
        return $"{Modifiers(bind.Modifiers)}|{bind.Key}|{bind.Dispatcher}|{bind.Arguments}|{Flags(bind.Flags)}";
    }

    private static string Modifiers(Modifier modifiers)
    {
        var names = new List<string>();
        if (modifiers.HasFlag(Modifier.Super))
            names.Add("SUPER");
        if (modifiers.HasFlag(Modifier.Alt))
            names.Add("ALT");
        if (modifiers.HasFlag(Modifier.Ctrl))
            names.Add("CTRL");
        if (modifiers.HasFlag(Modifier.Shift))
            names.Add("SHIFT");
        return string.Join(' ', names);
    }

    private static string Flags(BindFlag flags) => flags == BindFlag.None ? "" : flags.ToString();
}
