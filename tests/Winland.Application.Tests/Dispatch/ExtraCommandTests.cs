using System.Text.Json;

namespace Winland.Application.Tests;

public class ExtraCommandTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private const string SuperKill =
        """
        [{"locked":false,"mouse":false,"release":false,"repeat":false,"longPress":false,"non_consuming":false,"auto_consuming":false,"has_description":false,"modmask":64,"submap":"","submap_universal":"false","key":"Q","keycode":0,"catch_all":false,"description":"","allow_input_capture":false,"dispatcher":"killactive","arg":""}]
        """;

    [Fact]
    public void FlagPrefixSelectsJsonRelayoutAndUnmappedClients()
    {
        var json = Assert.IsType<CommandParsed>(CommandProtocol.Parse("j/binds"));
        Assert.IsType<BindsRequest>(json.Request);
        Assert.Equal(CommandFlags.Json, json.Request.Flags);

        var relayout = Assert.IsType<CommandParsed>(CommandProtocol.Parse("r/dispatch workspace 2"));
        var dispatch = Assert.IsType<DispatchRequest>(relayout.Request);
        Assert.Equal("workspace", dispatch.Name);
        Assert.Equal("2", dispatch.Arguments);
        Assert.Equal(CommandFlags.Relayout, dispatch.Flags);

        var all = Assert.IsType<CommandParsed>(CommandProtocol.Parse("a/clients"));
        Assert.True(Assert.IsType<ClientsRequest>(all.Request).All);

        var plain = Assert.IsType<CommandParsed>(CommandProtocol.Parse("clients"));
        Assert.False(Assert.IsType<ClientsRequest>(plain.Request).All);
        Assert.Equal("clients", ((CommandParsed)CommandProtocol.Parse("ja/clients")).Request is ClientsRequest request && request.All ? "clients" : "");
    }

    [Fact]
    public void PluginListIsAnUnknownCommand()
    {
        Assert.IsType<CommandRejected>(CommandProtocol.Parse("plugin list"));
        Assert.Equal("error: Unsupported keyword 'plugin'.", new WindowManager().Execute("keyword plugin plugins\\sample.dll").Text);
    }

    [Fact]
    public void BindsReportModifierBitsDescriptionsAndKeycodes()
    {
        var core = new WindowManager();
        Assert.Equal("[]", core.Execute("binds").Text);
        Assert.Equal("[]", core.Execute("j/binds").Text);

        Assert.Equal("ok", core.Execute("keyword bind SUPER, Q, killactive").Text);
        Assert.Equal(SuperKill, core.Execute("binds").Text);

        Assert.Equal("ok", core.Execute("keyword bind SUPER SHIFT, E, killactive").Text);
        var second = Json(core.Execute("binds").Text)[1];
        Assert.Equal(65, second.GetProperty("modmask").GetInt32());
        Assert.Equal("E", second.GetProperty("key").GetString());

        Assert.Equal("ok", core.Execute("keyword bindd SUPER, F, Close, killactive").Text);
        var described = Json(core.Execute("binds").Text)[2];
        Assert.True(described.GetProperty("has_description").GetBoolean());
        Assert.Equal("Close", described.GetProperty("description").GetString());

        Assert.Equal("ok", core.Execute("keyword bind SUPER, code:42, killactive").Text);
        var coded = Json(core.Execute("binds").Text)[3];
        Assert.Equal("", coded.GetProperty("key").GetString());
        Assert.Equal(42, coded.GetProperty("keycode").GetInt32());
    }

    [Fact]
    public void VersionCursorLockedAndLayoutsHaveTheirShapes()
    {
        var core = new WindowManager();
        core.Cursor = new FixedCursor(4, 9);
        core.SessionLock = new FixedLock(false);

        var version = Json(core.Execute("version").Text);
        Assert.Equal(BuildStamp.Version, version.GetProperty("version").GetString());
        Assert.Equal(BuildStamp.Commit, version.GetProperty("commit").GetString());
        Assert.Equal(BuildStamp.Tag, version.GetProperty("tag").GetString());
        Assert.Equal(JsonValueKind.Array, version.GetProperty("flags").ValueKind);
        Assert.StartsWith("{\"branch\":", core.Execute("version").Text, StringComparison.Ordinal);

        Assert.Equal("4, 9", core.Execute("cursorpos").Text);
        Assert.Equal("{\"x\":4,\"y\":9}", core.Execute("j/cursorpos").Text);
        Assert.Equal("{\"locked\":false}", core.Execute("locked").Text);
        core.SessionLock = new FixedLock(true);
        Assert.Equal("{\"locked\":true}", core.Execute("locked").Text);
        Assert.Equal("[\"dwindle\",\"master\",\"scrolling\",\"monocle\"]", core.Execute("layouts").Text);
    }

    [Fact]
    public void DisableAutoreloadStopsReloadingOnSaveUntilItIsTurnedOff()
    {
        var core = new WindowManager();
        Assert.True(core.ReloadsOnSave);

        core.Handle(new Configure(ConfigParser.Parse("misc:disable_autoreload = true\n")));
        Assert.False(core.ReloadsOnSave);
        Assert.Equal("{\"option\":\"misc:disable_autoreload\",\"bool\":true,\"set\":true}", core.Execute("getoption misc:disable_autoreload").Text);

        Assert.Equal("ok", core.Execute("keyword misc:disable_autoreload false").Text);
        Assert.True(core.ReloadsOnSave);
    }

    [Fact]
    public void ConfigErrorsKeepTheLastLoadAndGetOptionReadsHonoredDefaults()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("blur = on\n")));
        Assert.Equal("[\"line 1: Unsupported keyword 'blur'.\"]", core.Execute("configerrors").Text);
        Assert.Equal("{\"option\":\"general:border_size\",\"int\":2,\"set\":false}", core.Execute("getoption general:border_size").Text);
        Assert.Equal("{\"option\":\"dwindle:use_active_for_splits\",\"bool\":true,\"set\":false}", core.Execute("getoption dwindle:use_active_for_splits").Text);
        Assert.Equal("{\"option\":\"general:gaps_in\",\"css\":\"4 4 4 4\",\"set\":false}", core.Execute("getoption general:gaps_in").Text);
        Assert.Equal("no such option", core.Execute("getoption no:such").Text);

        Assert.Equal("ok", core.Execute("keyword general:border_size 4").Text);
        Assert.Equal("{\"option\":\"general:border_size\",\"int\":4,\"set\":true}", core.Execute("getoption general:border_size").Text);
        Assert.Equal("[\"line 1: Unsupported keyword 'blur'.\"]", core.Execute("configerrors").Text);
        Assert.Equal("ok", core.Execute("keyword misc:focus_on_activate true").Text);
        Assert.Equal("{\"option\":\"misc:focus_on_activate\",\"bool\":true,\"set\":true}", core.Execute("getoption misc:focus_on_activate").Text);
        Assert.Equal("error: Unsupported keyword 'blur'.", core.Execute("keyword blur on").Text);
        Assert.Equal("error: invalid keyword", core.Execute("keyword general:border_size").Text);
    }

    [Fact]
    public void KeyboardLayoutAndTheDevicesCommandAreNotApplied()
    {
        var core = new WindowManager();

        Assert.Equal("error: Unsupported keyword 'input:kb_variant'.", core.Execute("keyword input:kb_variant nodeadkeys").Text);
        Assert.Equal("error: invalid keyword", core.Execute("keyword input:kb_variant ").Text);
        Assert.Equal("error: unknown request", core.Execute("devices").Text);
        Assert.Equal("error: unknown request", core.Execute("j/devices").Text);
        Assert.Equal("error: unknown request", core.Execute("switchxkblayout all next").Text);
    }

    [Fact]
    public void GetOptionReportsGapsAsCssWithFourEdges()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("general:gaps_out = 10 20\n")));

        Assert.Equal("{\"option\":\"general:gaps_out\",\"css\":\"10 20 10 20\",\"set\":true}", core.Execute("getoption general:gaps_out").Text);
    }

    [Fact]
    public void GapsReturnErrorsAndPropsCanBeReadAndSet()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 200, 100)));

        Assert.Equal("ok", core.Execute("kill").Text);
        Assert.Equal("not enough args", core.Execute("getprop").Text);
        Assert.Equal("window not found", core.Execute("getprop missing opacity").Text);
        Assert.Equal("1", core.Execute("getprop active opacity").Text);
        Assert.Equal("{\"opacity\": 1}", core.Execute("j/getprop 0x2a opacity").Text);
        Assert.Equal("prop not found", core.Execute("getprop active float").Text);
        Assert.Equal("error: invalid prop name", core.Execute("setprop active float true").Text);

        Assert.Equal("ok", core.Execute("setprop active opacity 0.5").Text);
        Assert.Equal("0.5", core.Execute("getprop active opacity").Text);
        Assert.Equal("error: invalid opacity", core.Execute("setprop active opacity sideways").Text);
    }

    [Fact]
    public void BatchJoinsRepliesAndKeepsEscapedSemicolons()
    {
        var core = new WindowManager();
        var joined = core.Execute("[[BATCH]]layouts;locked").Text;
        Assert.Equal("[\"dwindle\",\"master\",\"scrolling\",\"monocle\"]\n\n\n{\"locked\":false}", joined);

        var escaped = core.Execute(@"[[BATCH]]keyword bind SUPER, Q, exec, [float\; tile] notepad;layouts").Text;
        Assert.Equal("ok\n\n\n[\"dwindle\",\"master\",\"scrolling\",\"monocle\"]", escaped);
        Assert.Contains("[float; tile] notepad", core.Execute("binds").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ABatchSplitsAtEverySemicolonAsThePinnedReleaseDoes()
    {
        Assert.Equal(["dispatch exec [float", "tile] a", "layouts"], ExtraCommands.SplitBatch("dispatch exec [float; tile] a;layouts"));
        Assert.Equal([@"a\b;c", @"d\.e", ""], ExtraCommands.SplitBatch(@" a\\b\;c ; d\.e ;"));
        Assert.Equal([@"x\"], ExtraCommands.SplitBatch(@"x\"));
        Assert.Equal([""], ExtraCommands.SplitBatch(""));
    }

    [Fact]
    public void RelayoutFlagStillRunsTheCommand()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));

        var reply = core.Execute("r/dispatch workspace 2");

        Assert.Equal("ok", reply.Text);
        Assert.Equal(new WorkspaceId("2"), reply.Effect.State.ActiveWorkspace);
    }

    [Fact]
    public void RelayoutFlagKeepsTheZOrderRequest()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));

        var reply = core.Execute("r/dispatch alterzorder top,address:0x1");

        Assert.Equal([new ZOrderRequest(new WindowId(1), true)], reply.Effect.ZOrder);
    }

    [Fact]
    public void ClientsOmitMinimizedWindowsUntilAllIsRequested()
    {
        var core = new WindowManager();
        var area = new Rectangle(0, 0, 400, 300);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));
        core.Handle(new WindowMinimized(new WindowId(2)));
        core.Execute("dispatch workspace 2");
        core.Handle(new WindowAppeared(new WindowId(3), area));

        var shown = Json(core.Execute("j/clients").Text).EnumerateArray().ToArray();
        Assert.Equal(2, shown.Length);
        Assert.Equal("0x1", shown[0].GetProperty("address").GetString());
        Assert.Equal("0x3", shown[1].GetProperty("address").GetString());

        var all = Json(core.Execute("ja/clients").Text).EnumerateArray().ToArray();
        Assert.Equal(3, all.Length);
        Assert.Equal("0x2", all[1].GetProperty("address").GetString());
        Assert.True(all[1].GetProperty("hidden").GetBoolean());
    }

    [Fact]
    public void ControlToolMapsFlagsBatchAndSubcommands()
    {
        Assert.True(ControlInvocation.TryCreate(["-j", "binds"], out var line, out var events));
        Assert.False(events);
        Assert.Equal("j/binds", line);

        Assert.True(ControlInvocation.TryCreate(["--batch", "layouts ; locked"], out line, out events));
        Assert.Equal("[[BATCH]]layouts ; locked", line);

        Assert.True(ControlInvocation.TryCreate(["-j", "--batch", "layouts ; locked"], out line, out events));
        Assert.Equal("[[BATCH]]j/layouts ;j/locked", line);

        Assert.True(ControlInvocation.TryCreate(["-j", "--batch", @"notify a\; b; keyword c\\; clients"], out line, out events));
        Assert.Equal(@"[[BATCH]]j/notify a\; b;j/keyword c\\;j/clients", line);

        Assert.True(ControlInvocation.TryCreate(["-a", "clients"], out line, out events));
        Assert.Equal("a/clients", line);

        Assert.True(ControlInvocation.TryCreate(["getoption", "general:border_size"], out line, out events));
        Assert.Equal("getoption general:border_size", line);

        Assert.True(ControlInvocation.TryCreate(["keyword", "general:border_size", "4"], out line, out events));
        Assert.Equal("keyword general:border_size 4", line);

        Assert.True(ControlInvocation.TryCreate(["dispatch", "resizeactive", "-20", "0"], out line, out events));
        Assert.Equal("dispatch resizeactive -20 0", line);

        Assert.True(ControlInvocation.TryCreate(["setprop", "active", "float", "true"], out line, out events));
        Assert.Equal("setprop active float true", line);

        Assert.True(ControlInvocation.TryCreate(["events"], out line, out events));
        Assert.True(events);
        Assert.False(ControlInvocation.TryCreate(["nope"], out _, out _));
        Assert.False(ControlInvocation.TryCreate(["eval", "1"], out _, out _));
        Assert.False(ControlInvocation.TryCreate(["repl"], out _, out _));
        Assert.False(ControlInvocation.TryCreate(["plugin", "list"], out _, out _));
        Assert.False(ControlInvocation.TryCreate(["plugin", "install", "https://example.com/plugins.git"], out _, out _));
        Assert.False(ControlInvocation.TryCreate(["--batch"], out _, out _));
    }

    [Theory]
    [InlineData(new[] { "dispatch", "splitratio", "-0.1" }, "dispatch splitratio -0.1")]
    [InlineData(new[] { "dispatch", "moveactive", "-50,", "0" }, "dispatch moveactive -50, 0")]
    [InlineData(new[] { "dispatch", "resizeactive", "-1.5,", "-2.25" }, "dispatch resizeactive -1.5, -2.25")]
    [InlineData(new[] { "-j", "dispatch", "splitratio", "-12" }, "j/dispatch splitratio -12")]
    public void ControlToolPassesNegativeNumbersThrough(string[] args, string expected)
    {
        Assert.True(ControlInvocation.TryCreate(args, out var line, out _));
        Assert.Equal(expected, line);
    }

    [Theory]
    [InlineData("-x")]
    [InlineData("-1.")]
    [InlineData("-1.2.3")]
    [InlineData("-")]
    public void ControlToolRefusesAFlagThatIsNotANumber(string arg)
    {
        Assert.False(ControlInvocation.TryCreate(["dispatch", "splitratio", arg], out _, out _));
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private sealed class FixedCursor(int x, int y) : ICursorPosition
    {
        public Point Read() => new(x, y);
    }

    [Fact]
    public void ABatchKeepsTheLaunchOfEveryExec()
    {
        var effect = new WindowManager().Execute("[[BATCH]]dispatch exec a ; dispatch exec b").Effect;

        Assert.Equal(["a", "b"], effect.Launches.Select(launch => launch.CommandLine));
    }

    [Fact]
    public void ABatchKeepsThePlacementOfEveryMovedFloatingWindow()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrulev2 = float, class:^(box)$\n"));
        foreach (var window in new long[] { 1, 2 })
        {
            core.Handle(new WindowAppeared(new WindowId(window), new Rectangle(0, 0, 1920, 1080))
            {
                Traits = WindowTraits.Normal with { Class = "box" },
                Bounds = new Rectangle(5, 7, 200, 150),
            });
        }

        var effect = core.Execute(
            "[[BATCH]]dispatch movewindowpixel exact 10 20,address:0x1 ; dispatch movewindowpixel exact 300 400,address:0x2").Effect;

        Assert.Equal(new Rectangle(10, 20, 200, 150), PlacedAt(effect, 1));
        Assert.Equal(new Rectangle(300, 400, 200, 150), PlacedAt(effect, 2));
    }

    private static DisplayPowerRequest[] Unfaded(IReadOnlyList<DisplayPowerRequest> requests) => [.. requests];

    [Fact]
    public void ABatchKeepsEveryDisplayPowerRequest()
    {
        var core = new WindowManager();
        core.Handle(new MonitorsChanged(
        [
            new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080)),
            new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, true, new Rectangle(1920, 0, 1280, 1024)),
        ]));

        // In a batch a backslash is written \\.
        var effect = core.Execute(@"[[BATCH]]dispatch dpms off \\\\.\\DISPLAY1 ; dispatch dpms on \\\\.\\DISPLAY2").Effect;

        Assert.Equal(
            [
                new DisplayPowerRequest(@"\\.\DISPLAY1", false),
                new DisplayPowerRequest(@"\\.\DISPLAY2", true),
            ],
            Unfaded(effect.DisplayPower));
    }

    [Fact]
    public void KeywordAddsAWindowRuleAndKeepsTheOnesAlreadyRead()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("windowrulev2 = float, class:^(first)$\n")));

        Assert.Equal("ok", core.Execute("keyword windowrulev2 float, class:^(second)$").Text);
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { Class = "first" } });
        var opened = core.Handle(new WindowAppeared(new WindowId(2), Area) { Traits = WindowTraits.Normal with { Class = "second" } });

        Assert.All(opened.State.Clients, client => Assert.True(client.Floating));
    }

    [Fact]
    public void KeywordAddsAWorkspaceRuleAndKeepsTheOnesAlreadyRead()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse("workspace = 1, gapsout:0\n")));

        Assert.Equal("ok", core.Execute("keyword workspace 2, gapsout:7").Text);

        var rules = core.Execute("j/workspacerules").Text;
        Assert.Contains("\"workspaceString\":\"1\"", rules, StringComparison.Ordinal);
        Assert.Contains("\"workspaceString\":\"2\"", rules, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("exec", "notepad")]
    [InlineData("exec-once", "notepad")]
    [InlineData("source", "other.conf")]
    public void KeywordDoesNotLaunchOrLoad(string keyword, string value)
    {
        var reply = new WindowManager().Execute($"keyword {keyword} {value}");

        Assert.Equal($"error: keyword '{keyword}' is not applied live", reply.Text);
        Assert.Empty(reply.Effect.Launches);
    }

    private static Rectangle PlacedAt(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        return Assert.Single(outcome.Placements, placement => placement.Client == client.Id).Bounds;
    }

    private sealed class FixedLock(bool locked) : ISessionLock
    {
        public bool Locked { get; } = locked;
    }
}
