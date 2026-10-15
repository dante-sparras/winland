using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class ExtraCommandTests
{
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
    public void BindsReportModifierBitsDescriptionsAndKeycodes()
    {
        var core = new Core();
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
    public void VersionCursorLockedLayoutsAndSystemInfoHaveTheirShapes()
    {
        var core = new Core();
        core.Cursor = new FixedCursor(4, 9);
        core.SessionLock = new FixedLock(false);

        var version = Json(core.Execute("version").Text);
        Assert.Equal(BuildStamp.Version, version.GetProperty("version").GetString());
        Assert.Equal(BuildStamp.Commit, version.GetProperty("commit").GetString());
        Assert.Equal(BuildStamp.Tag, version.GetProperty("tag").GetString());
        Assert.Equal(JsonValueKind.Array, version.GetProperty("flags").ValueKind);
        Assert.StartsWith("{\"branch\":", core.Execute("version").Text, StringComparison.Ordinal);

        Assert.Equal("{\"x\":4,\"y\":9}", core.Execute("cursorpos").Text);
        Assert.Equal("{\"locked\":false}", core.Execute("locked").Text);
        core.SessionLock = new FixedLock(true);
        Assert.Equal("{\"locked\":true}", core.Execute("locked").Text);
        Assert.Equal("[\"dwindle\",\"master\",\"scrolling\",\"monocle\"]", core.Execute("layouts").Text);

        var info = core.Execute("systeminfo").Text;
        Assert.StartsWith("Winland ", info, StringComparison.Ordinal);
        Assert.Contains("\nOS: ", info, StringComparison.Ordinal);
        Assert.Contains("\n.NET: ", info, StringComparison.Ordinal);
        Assert.Contains("\nMonitors: none", info, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigErrorsKeepTheLastLoadAndGetOptionReadsHonoredDefaults()
    {
        var core = new Core(ConfigParser.Parse("blur = on\n"));
        Assert.Equal("[\"line 1: Unsupported keyword 'blur'.\"]", core.Execute("configerrors").Text);
        Assert.Equal("{\"option\":\"general:border_size\",\"int\":1,\"set\":false}", core.Execute("getoption general:border_size").Text);
        Assert.Equal("{\"option\":\"dwindle:use_active_for_splits\",\"bool\":true,\"set\":false}", core.Execute("getoption dwindle:use_active_for_splits").Text);
        Assert.Equal("{\"option\":\"general:gaps_in\",\"str\":\"5\",\"set\":false}", core.Execute("getoption general:gaps_in").Text);
        Assert.Equal("no such option", core.Execute("getoption no:such").Text);

        Assert.Equal("ok", core.Execute("keyword general:border_size 4").Text);
        Assert.Equal("{\"option\":\"general:border_size\",\"int\":4,\"set\":true}", core.Execute("getoption general:border_size").Text);
        Assert.Equal("[\"line 1: Unsupported keyword 'blur'.\"]", core.Execute("configerrors").Text);
        Assert.Equal("error: keyword 'misc:focus_on_activate' is not applied live", core.Execute("keyword misc:focus_on_activate true").Text);
        Assert.Equal("error: Unsupported keyword 'blur'.", core.Execute("keyword blur on").Text);
        Assert.Equal("error: invalid keyword", core.Execute("keyword general:border_size").Text);
    }

    [Fact]
    public void GapsReturnErrorsAndFloatCanBeReadAndSet()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 200, 100)));

        Assert.Equal("error: click to kill is not supported", core.Execute("kill").Text);
        Assert.Equal("error: notifications are not supported", core.Execute("notify 1 1 0 hi").Text);
        Assert.Equal("error: notifications are not supported", core.Execute("dismissnotify").Text);
        Assert.Equal("error: rolling log is not supported", core.Execute("rollinglog").Text);
        Assert.Equal("error: not enough args", core.Execute("getprop").Text);
        Assert.Equal("error: window not found", core.Execute("getprop missing float").Text);
        Assert.Equal("{\"float\":false}", core.Execute("getprop active float").Text);
        Assert.Equal("{\"float\":false}", core.Execute("getprop 0x2a float").Text);
        Assert.Equal("{\"pin\":false}", core.Execute("getprop active pin").Text);
        Assert.Equal("error: prop not supported", core.Execute("getprop active opacity").Text);
        Assert.Equal("error: prop not supported", core.Execute("setprop active pin true").Text);

        Assert.Equal("ok", core.Execute("setprop active float true").Text);
        Assert.Equal("{\"float\":true}", core.Execute("getprop active floating").Text);
        Assert.Equal("error: invalid value", core.Execute("setprop active float sideways").Text);
    }

    [Fact]
    public void BatchJoinsRepliesAndKeepsSemicolonsInsideBrackets()
    {
        var core = new Core();
        var joined = core.Execute("[[BATCH]]layouts;locked").Text;
        Assert.Equal("[\"dwindle\",\"master\",\"scrolling\",\"monocle\"]\n\n\n{\"locked\":false}", joined);

        var bracketed = core.Execute("[[BATCH]]keyword bind SUPER, Q, exec, [float; tile] notepad;layouts").Text;
        Assert.Equal("ok\n\n\n[\"dwindle\",\"master\",\"scrolling\",\"monocle\"]", bracketed);
        Assert.Contains("exec", core.Execute("binds").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RelayoutFlagStillRunsTheCommand()
    {
        var core = new Core();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));

        var reply = core.Execute("r/dispatch workspace 2");

        Assert.Equal("ok", reply.Text);
        Assert.Equal(new WorkspaceId("2"), reply.Effect.State.ActiveWorkspace);
    }

    [Fact]
    public void ClientsOmitMinimizedWindowsUntilAllIsRequested()
    {
        var core = new Core();
        var area = new Rectangle(0, 0, 400, 300);
        core.Handle(new WindowAppeared(new WindowId(1), area));
        core.Handle(new WindowAppeared(new WindowId(2), area));
        core.Handle(new WindowMinimized(new WindowId(2)));
        core.Execute("dispatch workspace 2");
        core.Handle(new WindowAppeared(new WindowId(3), area));

        var shown = Json(core.Execute("clients").Text).EnumerateArray().ToArray();
        Assert.Equal(2, shown.Length);
        Assert.Equal("0x1", shown[0].GetProperty("address").GetString());
        Assert.Equal("0x3", shown[1].GetProperty("address").GetString());

        var all = Json(core.Execute("a/clients").Text).EnumerateArray().ToArray();
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
        Assert.False(ControlInvocation.TryCreate(["--batch"], out _, out _));
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

    private sealed class FixedLock(bool locked) : ISessionLock
    {
        public bool Locked { get; } = locked;
    }
}
