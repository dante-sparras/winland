using Winland;

namespace Winland.Tests;

public class ConfigTests
{
    [Fact]
    public void EmptyTextLoads()
    {
        var document = ConfigParser.Parse("");

        Assert.Empty(document.Settings);
        Assert.Empty(document.Binds);
        Assert.Empty(document.Warnings);
    }

    [Fact]
    public void NullTextIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigParser.Parse(null!));
    }

    [Fact]
    public void AssignmentStoresTheKeywordAndValue()
    {
        var document = ConfigParser.Parse("  general:gaps_in   =   5  \nexec-once = notepad\nexec-once = explorer\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new[]
            {
                new Setting("general:gaps_in", "5"),
                new Setting("exec-once", "notepad"),
                new Setting("exec-once", "explorer"),
            },
            document.Settings);
    }

    [Theory]
    [InlineData("exec")]
    [InlineData("exec-once")]
    [InlineData("execr")]
    [InlineData("execr-once")]
    [InlineData("exec-shutdown")]
    [InlineData("monitor")]
    public void HandlersCanRepeat(string keyword)
    {
        var document = ConfigParser.Parse($"{keyword} = one\n{keyword} = two\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(new[] { new Setting(keyword, "one"), new Setting(keyword, "two") }, document.Settings);
    }

    [Theory]
    [InlineData("windowrule", "float, ^(one)$", "tile, ^(two)$")]
    [InlineData("windowrulev2", "float, class:^(one)$", "tile, class:^(two)$")]
    public void WindowRulesCanRepeat(string keyword, string first, string second)
    {
        var document = ConfigParser.Parse($"{keyword} = {first}\n{keyword} = {second}\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(new[] { new Setting(keyword, first), new Setting(keyword, second) }, document.Settings);
        Assert.Equal(2, document.WindowRules.Count);
    }

    [Fact]
    public void MalformedWindowRuleWarnsAndTheConfigStillLoads()
    {
        var document = ConfigParser.Parse("windowrulev2 = dim_around, class:^(kitty)$\nwindowrulev2 = float, class:^(unclosed\ngeneral:gaps_in = 2\n");

        Assert.Equal(
            new[]
            {
                new Setting("windowrulev2", "dim_around, class:^(kitty)$"),
                new Setting("windowrulev2", "float, class:^(unclosed"),
                new Setting("general:gaps_in", "2"),
            },
            document.Settings);
        Assert.Empty(document.WindowRules);
        Assert.Equal(
            new[]
            {
                new ConfigWarning(1, "Unsupported window rule 'dim_around'."),
                new ConfigWarning(2, "Invalid window rule pattern."),
            },
            document.Warnings);
    }

    [Fact]
    public void XwaylandWindowRuleWarnsAndIsSkipped()
    {
        var document = ConfigParser.Parse("windowrulev2 = float, xwayland:1, class:^(kitty)$\n");

        Assert.Empty(document.WindowRules);
        Assert.Equal(
            new ConfigWarning(1, "Window rules that match on xwayland are not honored."),
            Assert.Single(document.Warnings));
        Assert.Equal(new Setting("windowrulev2", "float, xwayland:1, class:^(kitty)$"), Assert.Single(document.Settings));
    }

    [Fact]
    public void UnsupportedWindowRuleMatchDropsTheRule()
    {
        var document = ConfigParser.Parse("windowrulev2 = float, class:^(kitty)$, fullscreen:1\n");

        Assert.Empty(document.WindowRules);
        Assert.Equal(
            new ConfigWarning(1, "Unsupported window rule match 'fullscreen'."),
            Assert.Single(document.Warnings));
    }

    [Fact]
    public void CommentTextIsIgnored()
    {
        var text = "# full line\ngeneral:gaps_in = 5 # trailing\ngeneral:layout = dwindle ## hash\n";

        var document = ConfigParser.Parse(text);

        Assert.Equal(
            new ConfigWarning(3, "Layout 'dwindle # hash' is not honored."),
            Assert.Single(document.Warnings));
        Assert.Equal(
            new[]
            {
                new Setting("general:gaps_in", "5"),
                new Setting("general:layout", "dwindle # hash"),
            },
            document.Settings);
    }

    [Fact]
    public void CategoryBracesPrefixNestedKeywords()
    {
        var braced = ConfigParser.Parse("general {\n    gaps_in = 5\n    col.active_border = rgba(33ccffee)\n}\n");
        var flat = ConfigParser.Parse("general:gaps_in = 5\ngeneral:col.active_border = rgba(33ccffee)\n");

        Assert.Empty(braced.Warnings);
        Assert.Empty(flat.Warnings);
        Assert.Equal(flat.Settings, braced.Settings);
    }

    [Fact]
    public void NestedGapKeywordWarnsAndSiblingSettingsRemain()
    {
        var text = "decoration {\n    rounding = 10\n    blur {\n        passes = 3\n    }\n}\n";

        var document = ConfigParser.Parse(text);

        Assert.Equal(new Setting("decoration:rounding", "10"), Assert.Single(document.Settings));
        Assert.Equal(
            new ConfigWarning(4, "Unsupported keyword 'decoration:blur:passes'."),
            Assert.Single(document.Warnings));
    }

    [Theory]
    [InlineData("general")]
    [InlineData("decoration")]
    [InlineData("dwindle")]
    [InlineData("binds")]
    [InlineData("windowrule")]
    [InlineData("windowrulev2")]
    public void UnhonoredCategoryKeywordWarnsAndIsSkipped(string category)
    {
        var document = ConfigParser.Parse($"{category} {{\n    sample = 1\n}}\n");

        Assert.Empty(document.Settings);
        Assert.Equal(
            new ConfigWarning(2, $"Unsupported keyword '{category}:sample'."),
            Assert.Single(document.Warnings));
    }

    [Fact]
    public void HonoredKeywordsLoadWithoutAWarning()
    {
        var text = """
            general:gaps_in = 5
            general:gaps_out = 10
            general:border_size = 2
            general:col.active_border = rgba(ffffffff)
            general:col.inactive_border = rgba(444444ff)
            general:layout = dwindle
            dwindle:force_split = 0
            dwindle:preserve_split = true
            dwindle:smart_split = false
            dwindle:permanent_direction_override = false
            dwindle:split_width_multiplier = 1
            dwindle:use_active_for_splits = true
            dwindle:default_split_ratio = 1
            dwindle:split_bias = 0
            decoration:rounding = 0
            binds:drag_threshold = 0
            binds:scroll_event_delay = 300
            misc:focus_on_activate = false
            exec = app
            exec-once = app
            execr = app
            execr-once = app
            exec-shutdown = app
            env = BROWSER,firefox
            monitor = ,preferred,auto,auto
            windowrule = float, ^(kitty)$
            windowrulev2 = tile, class:^(kitty)$
            """;

        var document = ConfigParser.Parse(text);

        Assert.Empty(document.Warnings);
    }

    [Fact]
    public void UnhonoredKeywordInASourceWarnsWithThatSource()
    {
        var source = new MemorySource(new Dictionary<string, string>
        {
            ["extra.conf"] = "general:not_a_thing = 1\n",
        });

        var document = ConfigParser.Parse("source = extra.conf\n", source);

        Assert.Empty(document.Settings);
        Assert.Equal(
            new ConfigWarning(1, "Unsupported keyword 'general:not_a_thing'.", "extra.conf"),
            Assert.Single(document.Warnings));
    }

    [Fact]
    public void VariablesExpandWhereTheyAreUsed()
    {
        var text = "$b = 1\n$a = $b\ngeneral:gaps_in = $a\n$a = 2\ngeneral:gaps_out = $a\n$name = gaps_in\ngeneral:$name = 9\n";

        var document = ConfigParser.Parse(text);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new[]
            {
                new Setting("general:gaps_in", "1"),
                new Setting("general:gaps_out", "2"),
                new Setting("general:gaps_in", "9"),
            },
            document.Settings);
    }

    [Fact]
    public void LongerVariableNamesExpandBeforeShorterOnes()
    {
        var text = "$main = SUPER\n$mainMod = ALT\nbind = $mainMod, Q, killactive\n";

        var document = ConfigParser.Parse(text);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new Bind(Modifier.Alt, "Q", "killactive", "", BindFlag.None, null, null),
            Assert.Single(document.Binds));
    }

    [Fact]
    public void CyclicVariablesWarnAndTheConfigStillLoads()
    {
        var document = ConfigParser.Parse("$a = $b\n$b = $a\ngeneral:layout = $a\ngeneral:gaps_in = 1\n");

        Assert.Equal(new ConfigWarning(3, "Variable expansion did not finish."), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("general:gaps_in", "1"), Assert.Single(document.Settings));
    }

    [Fact]
    public void UndefinedVariablesStayInTheValue()
    {
        var document = ConfigParser.Parse("general:layout = $PATH\n");

        Assert.Equal(new Setting("general:layout", "$PATH"), Assert.Single(document.Settings));
        Assert.Equal(new ConfigWarning(1, "Layout '$PATH' is not honored."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void SourcedFileIsParsedInPlace()
    {
        var source = new MemorySource(new Dictionary<string, string>
        {
            ["colors.conf"] = "general:gaps_in = $gap\n",
        });

        var document = ConfigParser.Parse("$gap = 9\n$file = colors.conf\nsource=$file\n", source);

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("general:gaps_in", "9"), Assert.Single(document.Settings));
    }

    [Fact]
    public void SourceInsideACategoryInheritsIt()
    {
        var source = new MemorySource(new Dictionary<string, string>
        {
            ["inner.conf"] = "gaps_in = 4\n",
        });

        var document = ConfigParser.Parse("general {\n    source = inner.conf\n    gaps_out = 8\n}\n", source);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new[] { new Setting("general:gaps_in", "4"), new Setting("general:gaps_out", "8") },
            document.Settings);
    }

    [Fact]
    public void MissingSourceWarnsAndTheConfigStillLoads()
    {
        var document = ConfigParser.Parse("source = missing.conf\ngeneral:gaps_in = 4\n");

        Assert.Equal(new ConfigWarning(1, "Could not read source 'missing.conf'."), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("general:gaps_in", "4"), Assert.Single(document.Settings));
    }

    [Fact]
    public void MalformedLineInASourceKeepsItsOwnLineNumber()
    {
        var source = new MemorySource(new Dictionary<string, string>
        {
            ["bad.conf"] = "general:gaps_in = 1\nnot a line\n",
        });

        var document = ConfigParser.Parse("source = bad.conf\n", source);

        Assert.Equal(new Setting("general:gaps_in", "1"), Assert.Single(document.Settings));
        Assert.Equal(new ConfigWarning(2, "Malformed line.", "bad.conf"), Assert.Single(document.Warnings));
    }

    [Fact]
    public void SourceCycleWarnsAndDoesNotLoop()
    {
        var source = new MemorySource(new Dictionary<string, string>
        {
            ["a.conf"] = "source = b.conf\n",
            ["b.conf"] = "source = a.conf\ngeneral:gaps_in = 3\n",
        });

        var document = ConfigParser.Parse("source = a.conf\n", source);

        Assert.Equal(new ConfigWarning(1, "Source 'a.conf' includes itself.", "b.conf"), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("general:gaps_in", "3"), Assert.Single(document.Settings));
    }

    [Fact]
    public void MalformedLineWarnsWithItsLineNumber()
    {
        var document = ConfigParser.Parse("general:gaps_in = 5\r\nthis is bad\r\nexec = notepad\r\n}\n");

        Assert.Equal(
            new[]
            {
                new ConfigWarning(2, "Malformed line."),
                new ConfigWarning(4, "Malformed line."),
            },
            document.Warnings);
        Assert.Equal(
            new[] { new Setting("general:gaps_in", "5"), new Setting("exec", "notepad") },
            document.Settings);
    }

    [Fact]
    public void UnsupportedKeywordWarnsAndIsSkipped()
    {
        var text = "general:gaps_in = 5\nghostkeyword = path\nxwayland:enabled = true\ngeneral:border_size = 2\n";

        var document = ConfigParser.Parse(text);

        Assert.Equal(
            new[]
            {
                new ConfigWarning(2, "Unsupported keyword 'ghostkeyword'."),
                new ConfigWarning(3, "Unsupported keyword 'xwayland:enabled'."),
            },
            document.Warnings);
        Assert.Equal(
            new[] { new Setting("general:gaps_in", "5"), new Setting("general:border_size", "2") },
            document.Settings);
    }

    [Fact]
    public void PluginAssignmentIsKept()
    {
        var document = ConfigParser.Parse("plugin = plugins\\sample.dll\ngeneral:gaps_in = 2\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new[] { new Setting("plugin", "plugins\\sample.dll"), new Setting("general:gaps_in", "2") },
            document.Settings);
    }

    [Fact]
    public void GapKeywordsMatchTheGapsDocumentAndAreSkipped()
    {
        var documented = ReadGaps(File.ReadAllText(GapsDocumentPath()));

        Assert.Equal(
            ConfigGaps.All.Select(gap => (gap.Keyword, gap.Reason)).OrderBy(gap => gap.Keyword),
            documented.OrderBy(gap => gap.Keyword));

        foreach (var gap in ConfigGaps.All)
        {
            var document = ConfigParser.Parse($"{gap.Keyword} = 1\ngeneral:gaps_in = 2\n");

            Assert.Equal(new Setting("general:gaps_in", "2"), Assert.Single(document.Settings));
            Assert.Equal(new ConfigWarning(1, $"Unsupported keyword '{gap.Keyword}'."), Assert.Single(document.Warnings));
        }
    }

    [Fact]
    public void UnclosedCategoryWarnsAndTheSettingRemains()
    {
        var document = ConfigParser.Parse("general {\n    gaps_in = 5\n");

        Assert.Equal(new Setting("general:gaps_in", "5"), Assert.Single(document.Settings));
        Assert.Equal(new ConfigWarning(1, "Unclosed category 'general'."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void UnclosedCategoryInASourceDoesNotLeak()
    {
        var source = new MemorySource(new Dictionary<string, string>
        {
            ["inner.conf"] = "dwindle {\n    preserve_split = true\n",
        });

        var document = ConfigParser.Parse("source = inner.conf\ngeneral:gaps_in = 1\n", source);

        Assert.Equal(new ConfigWarning(1, "Unclosed category 'dwindle'.", "inner.conf"), Assert.Single(document.Warnings));
        Assert.Equal(
            new[] { new Setting("dwindle:preserve_split", "true"), new Setting("general:gaps_in", "1") },
            document.Settings);
    }

    [Fact]
    public void BackslashJoinsTheNextLine()
    {
        var document = ConfigParser.Parse("general:gaps_in = \\\n    15\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("general:gaps_in", "15"), Assert.Single(document.Settings));
    }

    [Fact]
    public void ACommentEndingInABackslashDoesNotSwallowTheNextLine()
    {
        var document = ConfigParser.Parse("# path C:\\dir\\\ngeneral:gaps_in = 15\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("general:gaps_in", "15"), Assert.Single(document.Settings));
    }

    [Fact]
    public void AnInlineCommentEndingInABackslashWarnsAndDoesNotSwallowTheNextLine()
    {
        var document = ConfigParser.Parse("general:gaps_out = 4 # C:\\dir\\\ngeneral:gaps_in = 15\n");

        Assert.Equal(new ConfigWarning(1, "A backslash at the end of a comment does not join the next line."), Assert.Single(document.Warnings));
        Assert.Equal(
            new[] { new Setting("general:gaps_out", "4"), new Setting("general:gaps_in", "15") },
            document.Settings);
    }

    [Fact]
    public void AnEscapedHashBeforeABackslashStillJoinsTheNextLine()
    {
        var document = ConfigParser.Parse("exec = echo ## \\\n    done\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("exec", "echo #    done"), Assert.Single(document.Settings));
    }

    [Fact]
    public void DanglingBackslashWarns()
    {
        var document = ConfigParser.Parse("general:gaps_in = 5\\");

        Assert.Equal(new ConfigWarning(1, "Line ends with a backslash."), Assert.Single(document.Warnings));
        Assert.Empty(document.Settings);
    }

    [Fact]
    public void EmptyDispatcherParsesWithoutAWarning()
    {
        var document = ConfigParser.Parse("bind = SUPER, Q,\nbindd = , Super_L, Menu, \n");

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new BindDirective[]
            {
                new Bind(Modifier.Super, "Q", "", "", BindFlag.None, null, null),
                new Bind(Modifier.None, "Super_L", "", "", BindFlag.None, "Menu", null),
            },
            document.Binds);
    }

    [Fact]
    public void BindIsParsedIntoModifiersKeyAndDispatcher()
    {
        var text = "$mainMod = SUPER\nbind = $mainMod, Q, killactive\nbind = WIN SHIFT, code:28, exec, notepad --title hello\n";

        var document = ConfigParser.Parse(text);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new BindDirective[]
            {
                new Bind(Modifier.Super, "Q", "killactive", "", BindFlag.None, null, null),
                new Bind(Modifier.Super | Modifier.Shift, "code:28", "exec", "notepad --title hello", BindFlag.None, null, null),
            },
            document.Binds);
    }

    [Fact]
    public void BindFlagsAndDescriptionAreParsed()
    {
        var text =
            "binde = SUPER, H, movefocus, l\n" +
            "bindl = , XF86AudioMute, exec, wpctl set-mute\n" +
            "bindm = SUPER, mouse:272, movewindow\n" +
            "bindd = SUPER, Return, Open a terminal, exec, kitty\n" +
            "bindc = SUPER, V, exec, notepad\n" +
            "bindg = SUPER, G, exec, notepad\n" +
            "bindel = SUPER SHIFT, E, exec, notepad\n" +
            "binds = SUPER, A&B, exec, notepad\n";

        var document = ConfigParser.Parse(text);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new BindDirective[]
            {
                new Bind(Modifier.Super, "H", "movefocus", "l", BindFlag.Repeat, null, null),
                new Bind(Modifier.None, "XF86AudioMute", "exec", "wpctl set-mute", BindFlag.Locked, null, null),
                new Bind(Modifier.Super, "mouse:272", "movewindow", "", BindFlag.Mouse, null, null),
                new Bind(Modifier.Super, "Return", "exec", "kitty", BindFlag.None, "Open a terminal", null),
                new Bind(Modifier.Super, "V", "exec", "notepad", BindFlag.Click | BindFlag.Release, null, null),
                new Bind(Modifier.Super, "G", "exec", "notepad", BindFlag.Drag | BindFlag.Release, null, null),
                new Bind(Modifier.Super | Modifier.Shift, "E", "exec", "notepad", BindFlag.Repeat | BindFlag.Locked, null, null),
                new Bind(Modifier.Super, "A&B", "exec", "notepad", BindFlag.MultiKey, null, null),
            },
            document.Binds);
    }

    [Theory]
    [InlineData("SUPER", Modifier.Super)]
    [InlineData("WIN", Modifier.Super)]
    [InlineData("LOGO", Modifier.Super)]
    [InlineData("MOD4", Modifier.Super)]
    [InlineData("ALT", Modifier.Alt)]
    [InlineData("MOD1", Modifier.Alt)]
    [InlineData("CTRL", Modifier.Ctrl)]
    [InlineData("CONTROL", Modifier.Ctrl)]
    [InlineData("SHIFT", Modifier.Shift)]
    [InlineData("META", Modifier.Super)]
    [InlineData("super", Modifier.Super)]
    [InlineData("SUPER_SHIFT", Modifier.Super | Modifier.Shift)]
    [InlineData("SUPER+SHIFT", Modifier.Super | Modifier.Shift)]
    [InlineData("SUPER + SHIFT", Modifier.Super | Modifier.Shift)]
    [InlineData("SUPERSHIFT", Modifier.Super | Modifier.Shift)]
    [InlineData("Ctrl_Alt", Modifier.Ctrl | Modifier.Alt)]
    [InlineData("SUPER SHIFT CTRL ALT", Modifier.Super | Modifier.Shift | Modifier.Ctrl | Modifier.Alt)]
    public void ModifierNamesAreParsed(string name, Modifier expected)
    {
        var document = ConfigParser.Parse($"bind = {name}, Q, killactive\n");

        Assert.Equal(
            new Bind(expected, "Q", "killactive", "", BindFlag.None, null, null),
            Assert.Single(document.Binds));
    }

    [Fact]
    public void AModifierVariableJoinedWithAnUnderscoreIsParsed()
    {
        var document = ConfigParser.Parse("$mainMod = SUPER\nbind = $mainMod_SHIFT, Q, killactive\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new Bind(Modifier.Super | Modifier.Shift, "Q", "killactive", "", BindFlag.None, null, null),
            Assert.Single(document.Binds));
    }

    [Theory]
    [InlineData("CAPS", "CAPS")]
    [InlineData("SUPER_MOD5", "MOD5")]
    [InlineData("MOD2", "MOD2")]
    [InlineData("MOD3", "MOD3")]
    public void AModifierWindowsCannotHoldWarns(string name, string modifier)
    {
        var document = ConfigParser.Parse($"bind = {name}, Q, killactive\n");

        Assert.Empty(document.Binds);
        Assert.Equal(new ConfigWarning(1, $"Modifier '{modifier}' is not honored."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void InvalidBindIsSkippedAndALaterBindLoads()
    {
        var text =
            "bindz = SUPER, Q, killactive\n" +
            "bind = SUPER, Q\n" +
            "bindre = SUPER, Q, killactive\n" +
            "bind = FOO, Q, killactive\n" +
            "bindm = SUPER, mouse:272, movewindow, extra\n" +
            "bind = SUPER, Q, killactive\n";

        var document = ConfigParser.Parse(text);

        Assert.Equal(
            new[]
            {
                new ConfigWarning(1, "Invalid bind flag 'z'."),
                new ConfigWarning(2, "Bind has too few arguments."),
                new ConfigWarning(3, "Bind flags conflict."),
                new ConfigWarning(4, "Invalid modifier 'FOO'."),
                new ConfigWarning(5, "Bind has too many arguments."),
            },
            document.Warnings);
        Assert.Equal(
            new Bind(Modifier.Super, "Q", "killactive", "", BindFlag.None, null, null),
            Assert.Single(document.Binds));
    }

    [Fact]
    public void UnbindIsParsedInOrder()
    {
        var text =
            "bind = SUPER, Q, killactive\n" +
            "unbind = SUPER, Q\n" +
            "bind = SUPER, R, killactive\n" +
            "unbind = all\n";

        var document = ConfigParser.Parse(text);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new BindDirective[]
            {
                new Bind(Modifier.Super, "Q", "killactive", "", BindFlag.None, null, null),
                new Unbind(Modifier.Super, "Q"),
                new Bind(Modifier.Super, "R", "killactive", "", BindFlag.None, null, null),
                new UnbindAll(),
            },
            document.Binds);
    }

    [Fact]
    public void SubmapIsAttachedUntilReset()
    {
        var text =
            "submap = resize\n" +
            "bind = SUPER, H, resizeactive, 10 0\n" +
            "submap = reset\n" +
            "bind = SUPER, Q, killactive\n";

        var document = ConfigParser.Parse(text);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new BindDirective[]
            {
                new Bind(Modifier.Super, "H", "resizeactive", "10 0", BindFlag.None, null, "resize"),
                new Bind(Modifier.Super, "Q", "killactive", "", BindFlag.None, null, null),
            },
            document.Binds);
    }

    [Fact]
    public void DwindleLayoutDoesNotWarn()
    {
        var document = ConfigParser.Parse("general:layout = dwindle\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("general:layout", "dwindle"), Assert.Single(document.Settings));
    }

    [Fact]
    public void MonocleLayoutDoesNotWarn()
    {
        var document = ConfigParser.Parse("general:layout = monocle\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("general:layout", "monocle"), Assert.Single(document.Settings));
    }

    [Fact]
    public void ScrollingLayoutDoesNotWarn()
    {
        var document = ConfigParser.Parse("general:layout = scrolling\nscrolling:column_width = 0.4\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new[] { new Setting("general:layout", "scrolling"), new Setting("scrolling:column_width", "0.4") },
            document.Settings);
    }

    [Theory]
    [InlineData("floating")]
    [InlineData("Dwindle")]
    [InlineData("")]
    public void OtherLayoutsWarnAndAreKept(string name)
    {
        var document = ConfigParser.Parse($"general {{\n    layout = {name}\n}}\n");

        Assert.Equal(new Setting("general:layout", name), Assert.Single(document.Settings));
        Assert.Equal(new ConfigWarning(2, $"Layout '{name}' is not honored."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void ExpandedLayoutNameUsesTheSameRule()
    {
        var honored = ConfigParser.Parse("$name = dwindle\ngeneral:layout = $name\n");
        var rejected = ConfigParser.Parse("$name = floating\ngeneral:layout = $name\n");

        Assert.Empty(honored.Warnings);
        Assert.Equal(new Setting("general:layout", "dwindle"), Assert.Single(honored.Settings));
        Assert.Equal(new ConfigWarning(2, "Layout 'floating' is not honored."), Assert.Single(rejected.Warnings));
    }

    [Fact]
    public void WorkspaceRulesAreReadAndGapFieldsWarn()
    {
        var document = ConfigParser.Parse("workspace = 1, monitor:DP-1, default:true, shadow:1\ngeneral:gaps_in = 2\n");

        Assert.DoesNotContain(document.Warnings, warning => warning.Message.Contains("'workspace'", StringComparison.Ordinal));
        Assert.Equal(new ConfigWarning(1, "A workspace rule's shadow is not honored."), Assert.Single(document.Warnings));
        Assert.Contains(document.Settings, setting => setting.Keyword == "workspace");
        Assert.Equal(new Setting("general:gaps_in", "2"), document.Settings[^1]);
        var rule = Assert.Single(document.WorkspaceRules);
        Assert.Equal("DP-1", rule.Monitor);
        Assert.True(rule.Default);
    }

    [Fact]
    public void SubmapBindInASourceIsKept()
    {
        var source = new MemorySource(new Dictionary<string, string>
        {
            ["keys.conf"] = "submap = resize\nbind = SUPER, H, resizeactive, 10 0\n",
        });

        var document = ConfigParser.Parse("source = keys.conf\n", source);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new Bind(Modifier.Super, "H", "resizeactive", "10 0", BindFlag.None, null, "resize"),
            Assert.Single(document.Binds));
    }

    private static string GapsDocumentPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "gaps.md");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException("docs/gaps.md");
    }

    private static List<(string Keyword, string Reason)> ReadGaps(string text)
    {
        var rows = new List<(string Keyword, string Reason)>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('|'))
                continue;

            var cells = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length != 2 || cells[0] == "keyword" || cells[0].All(character => character == '-'))
                continue;

            rows.Add((cells[0], cells[1]));
        }

        return rows;
    }

    private sealed class MemorySource(IReadOnlyDictionary<string, string> files) : IConfigSource
    {
        public string? Read(string path) => files.TryGetValue(path, out var text) ? text : null;
    }
}
