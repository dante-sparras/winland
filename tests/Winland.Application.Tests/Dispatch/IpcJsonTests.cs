using System.Text.Json;

namespace Winland.Application.Tests;

public class IpcJsonTests
{
    [Fact]
    public void ClientsAndActiveWindowMatchTheStatusBarShape()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 800, 600))
        {
            Traits = WindowTraits.Normal with { Class = "kitty", Title = "term" },
            ProcessId = 4242,
        });

        const string Client =
            """
            {"address":"0x2a","mapped":true,"hidden":false,"visible":true,"acceptsInput":true,"at":[0,0],"size":[800,600],"workspace":{"address":"1","id":1,"type":"normal","name":"1","monitor":""},"floating":false,"monitor":0,"class":"kitty","title":"term","initialClass":"kitty","initialTitle":"term","pid":4242,"xwayland":false,"pinned":false,"pinFullscreened":false,"fullscreen":0,"fullscreenClient":0,"fullscreenHandler":"default","allowedOverFullscreen":true,"grouped":[],"tags":[],"focusHistoryID":0,"xdgTag":"","xdgDescription":"","contentType":"none","tearingHint":false,"stableId":"2a"}
            """;

        Assert.Equal("[" + Client + "]", core.Execute("j/clients").Text);
        Assert.Equal(Client, core.Execute("j/activewindow").Text);
        Assert.Equal(4242, core.Recovery().Clients[0].ProcessId);
    }

    [Fact]
    public void FloatingClientReportsItsOwnRectangleAndAHiddenClientIsNotVisible()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));
        core.Handle(new WindowAppeared(new WindowId(7), new Rectangle(0, 0, 800, 600))
        {
            Traits = WindowTraits.Normal with { Dialog = true, Class = "dialog", Title = "Save" },
            Bounds = new Rectangle(10, 20, 300, 200),
            ProcessId = 99,
        });
        core.Execute("dispatch workspace 2");

        var clients = Json(core.Execute("j/clients").Text).EnumerateArray().ToArray();

        Assert.Equal("0x1", clients[0].GetProperty("address").GetString());
        Assert.True(clients[0].GetProperty("hidden").GetBoolean());
        Assert.False(clients[0].GetProperty("mapped").GetBoolean());
        Assert.False(clients[0].GetProperty("visible").GetBoolean());
        Assert.False(clients[0].GetProperty("acceptsInput").GetBoolean());
        Assert.Equal(0, clients[0].GetProperty("monitor").GetInt32());

        Assert.Equal("0x7", clients[1].GetProperty("address").GetString());
        Assert.True(clients[1].GetProperty("floating").GetBoolean());
        Assert.True(clients[1].GetProperty("hidden").GetBoolean());
        Assert.Equal(250, clients[1].GetProperty("at")[0].GetInt32());
        Assert.Equal(200, clients[1].GetProperty("at")[1].GetInt32());
        Assert.Equal(300, clients[1].GetProperty("size")[0].GetInt32());
        Assert.Equal(200, clients[1].GetProperty("size")[1].GetInt32());
        Assert.Equal(99, clients[1].GetProperty("pid").GetInt64());
        Assert.Equal("dialog", clients[1].GetProperty("initialClass").GetString());
        Assert.Equal("Save", clients[1].GetProperty("initialTitle").GetString());
        Assert.False(clients[1].GetProperty("xwayland").GetBoolean());
        Assert.Equal(JsonValueKind.Array, clients[1].GetProperty("grouped").ValueKind);
        Assert.Equal(0, clients[1].GetProperty("grouped").GetArrayLength());
        Assert.Equal(JsonValueKind.Number, clients[1].GetProperty("fullscreen").ValueKind);
        Assert.Equal(0, clients[1].GetProperty("fullscreen").GetInt32());
        Assert.Equal(0, clients[1].GetProperty("fullscreenClient").GetInt32());
    }

    [Fact]
    public void WorkspacesAndActiveWorkspaceMatchTheStatusBarShape()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 800, 600))
        {
            Traits = WindowTraits.Normal with { Class = "kitty", Title = "term" },
        });

        const string Workspace =
            """
            {"address":"1","id":1,"type":"normal","name":"1","monitor":"","monitorID":0,"windows":1,"hasfullscreen":false,"lastwindow":"0x2a","lastwindowtitle":"term","ispersistent":false,"tiledLayout":"dwindle"}
            """;

        Assert.Equal("[" + Workspace + "]", core.Execute("j/workspaces").Text);
        Assert.Equal(Workspace, core.Execute("j/activeworkspace").Text);
    }

    [Theory]
    [InlineData("master")]
    [InlineData("scrolling")]
    [InlineData("monocle")]
    [InlineData("dwindle")]
    public void WorkspacesReportTheirTiledLayout(string layout)
    {
        var core = new WindowManager(ConfigParser.Parse("general:layout = " + layout));
        core.Handle(new WindowAppeared(new WindowId(42), new Rectangle(0, 0, 800, 600)));

        var workspace = Assert.Single(Json(core.Execute("j/workspaces").Text).EnumerateArray());

        Assert.Equal(layout, workspace.GetProperty("tiledLayout").GetString());
        Assert.Equal(layout, Json(core.Execute("j/activeworkspace").Text).GetProperty("tiledLayout").GetString());
    }

    [Fact]
    public void ActiveWorkspaceRejectsTrailingArguments()
    {
        Assert.Equal("error: unknown request", new WindowManager().Execute("j/activeworkspace extra").Text);
    }

    [Fact]
    public void MonitorsMatchTheStatusBarShape()
    {
        var core = new WindowManager();
        var display = new Monitor(
            "Dell",
            "Dell U2720Q",
            0,
            0,
            1920,
            1080,
            1,
            true,
            new Rectangle(0, 0, 1920, 1040),
            Make: "DEL",
            Model: "U2720Q",
            Serial: "ABC123",
            RefreshRate: 60,
            PhysicalWidth: 597,
            PhysicalHeight: 336);
        core.Handle(new MonitorsChanged([display]));

        const string Monitor =
            """
            {"id":0,"name":"Dell","description":"DEL U2720Q ABC123","make":"DEL","model":"U2720Q","serial":"ABC123","width":1920,"height":1080,"physicalWidth":597,"physicalHeight":336,"refreshRate":60,"x":0,"y":0,"activeWorkspace":{"address":"1","id":1,"type":"normal","name":"1","monitor":"Dell"},"specialWorkspace":{"address":"","type":"","name":"","monitor":""},"reserved":[0,0,0,40],"scale":1,"transform":0,"focused":true,"dpmsStatus":true,"vrr":false,"solitary":"0","solitaryBlockedBy":null,"activelyTearing":false,"tearingBlockedBy":null,"directScanoutTo":"0","directScanoutBlockedBy":null,"disabled":false,"currentFormat":"","mirrorOf":"none","availableModes":["1920x1080@60.00Hz"],"colorManagementPreset":"srgb","sdrBrightness":1,"sdrSaturation":1,"sdrMinLuminance":0.2,"sdrMaxLuminance":80,"hardwareCursorsInUse":false,"hardwareDetails":{"backend":"windows","hdr":false,"chroma":false,"bt2020":false,"vrrCapable":false}}
            """;

        Assert.Equal("[" + Monitor + "]", core.Execute("j/monitors").Text);
        Assert.Equal("[" + Monitor + "]", core.Execute("j/monitors all").Text);
    }

    [Fact]
    public void ClientMonitorIdAgreesWithTheMonitorItIsOn()
    {
        var core = new WindowManager();
        var left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080), RefreshRate: 60);
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1.5, true, new Rectangle(1920, 0, 1280, 1024), RefreshRate: 144);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var monitors = Json(core.Execute("j/monitors").Text).EnumerateArray().ToArray();
        var clients = Json(core.Execute("j/clients").Text).EnumerateArray().ToArray();

        Assert.Equal(monitors[0].GetProperty("id").GetInt32(), clients[0].GetProperty("monitor").GetInt32());
        Assert.Equal(monitors[1].GetProperty("id").GetInt32(), clients[1].GetProperty("monitor").GetInt32());
        Assert.Equal(1, clients[1].GetProperty("monitor").GetInt32());
        Assert.Equal("1920x1080@60.00Hz", monitors[0].GetProperty("availableModes")[0].GetString());
        Assert.Equal(144, monitors[1].GetProperty("refreshRate").GetDouble());
        Assert.Equal(1.5, monitors[1].GetProperty("scale").GetDouble());
    }

    [Fact]
    public void MonitorsAllIncludesDisabledMonitors()
    {
        var core = new WindowManager();
        var left = new Monitor(@"\\.\DISPLAY1", "Dell", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080));
        var right = new Monitor(@"\\.\DISPLAY2", "Samsung", 1920, 0, 1280, 1024, 1, false, new Rectangle(1920, 0, 1280, 1024));
        core.Handle(new MonitorsChanged([left, right]));

        var shown = Json(core.Execute("j/monitors").Text).EnumerateArray().ToArray();
        var all = Json(core.Execute("j/monitors all").Text).EnumerateArray().ToArray();

        var enabled = Assert.Single(shown);
        Assert.Equal(@"\\.\DISPLAY1", enabled.GetProperty("name").GetString());
        Assert.False(enabled.GetProperty("disabled").GetBoolean());
        Assert.True(enabled.GetProperty("dpmsStatus").GetBoolean());

        Assert.Equal(2, all.Length);
        var disabled = Assert.Single(all, monitor => monitor.GetProperty("name").GetString() == @"\\.\DISPLAY2");
        Assert.True(disabled.GetProperty("disabled").GetBoolean());
        Assert.False(disabled.GetProperty("dpmsStatus").GetBoolean());
        Assert.False(disabled.GetProperty("focused").GetBoolean());
        Assert.Equal(JsonValueKind.Array, disabled.GetProperty("reserved").ValueKind);
        Assert.Equal(4, disabled.GetProperty("reserved").GetArrayLength());
        Assert.Equal(JsonValueKind.Object, disabled.GetProperty("specialWorkspace").ValueKind);
        Assert.False(disabled.GetProperty("specialWorkspace").TryGetProperty("id", out _));
        Assert.Equal("", disabled.GetProperty("specialWorkspace").GetProperty("name").GetString());
    }

    [Fact]
    public void MonitorsWithoutADisplayStillReportsTheActiveWorkspace()
    {
        const string Monitor =
            """
            {"id":0,"name":"","description":"","make":"","model":"","serial":"","width":0,"height":0,"physicalWidth":0,"physicalHeight":0,"refreshRate":0,"x":0,"y":0,"activeWorkspace":{"address":"1","id":1,"type":"normal","name":"1","monitor":""},"specialWorkspace":{"address":"","type":"","name":"","monitor":""},"reserved":[0,0,0,0],"scale":1,"transform":0,"focused":true,"dpmsStatus":true,"vrr":false,"solitary":"0","solitaryBlockedBy":null,"activelyTearing":false,"tearingBlockedBy":null,"directScanoutTo":"0","directScanoutBlockedBy":null,"disabled":false,"currentFormat":"","mirrorOf":"none","availableModes":[],"colorManagementPreset":"srgb","sdrBrightness":1,"sdrSaturation":1,"sdrMinLuminance":0.2,"sdrMaxLuminance":80,"hardwareCursorsInUse":false,"hardwareDetails":{"backend":"windows","hdr":false,"chroma":false,"bt2020":false,"vrrCapable":false}}
            """;

        Assert.Equal("[" + Monitor + "]", new WindowManager().Execute("j/monitors").Text);
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
