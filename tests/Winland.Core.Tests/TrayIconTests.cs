using Winland;

namespace Winland.Tests;

public class TrayIconTests
{
    [Fact]
    public void TheIconStartsOnWorkspaceOne()
    {
        var outcome = new Core().Execute("dispatch workspace 1").Effect;

        Assert.NotNull(outcome.Tray);
        Assert.True(outcome.Tray.Shown);
        Assert.Equal("1", outcome.Tray.Number);
        Assert.Equal("Workspace 1", outcome.Tray.Tip);
    }

    [Fact]
    public void SwitchingWorkspaceChangesTheNumber()
    {
        var core = new Core();

        var outcome = core.Execute("dispatch workspace 2").Effect;

        Assert.Equal(new WorkspaceId("2"), outcome.State.ActiveWorkspace);
        Assert.Equal("2", outcome.Tray!.Number);
    }

    [Fact]
    public void ANamedWorkspaceIsNotCreated()
    {
        var reply = new Core().Execute("dispatch workspace name:dev");

        Assert.Equal("error: no such workspace", reply.Text);
        Assert.Equal(WorkspaceId.Initial, reply.Effect.State.ActiveWorkspace);
        Assert.Equal("1", reply.Effect.Tray!.Number);
        Assert.DoesNotContain(reply.Effect.State.Workspaces, workspace => workspace.Id.Name == "dev");
    }

    [Fact]
    public void ASpecialWorkspaceKeepsTheNumberUnderneath()
    {
        var core = new Core();
        core.Execute("dispatch workspace 4");

        var opened = core.Execute("dispatch togglespecialworkspace scratch").Effect;

        Assert.Equal(new WorkspaceId("4"), opened.State.ActiveWorkspace);
        Assert.NotNull(opened.State.SpecialWorkspace);
        Assert.Equal("4", opened.Tray!.Number);
    }

    [Fact]
    public void ChangingTheWorkspaceIdChangesTheNumber()
    {
        var changed = new Core().Execute("dispatch changeworkspaceid 1 12").Effect;

        Assert.Equal("12", changed.Tray!.Number);
    }

    [Fact]
    public void TheFocusedMonitorsWorkspaceIsTheNumber()
    {
        var core = TwoMonitors();
        Assert.Equal(new WorkspaceId("1"), core.Execute("dispatch workspace 1").Effect.State.ActiveWorkspace);
        Assert.Equal("1", core.Execute("dispatch focusmonitor LEFT").Effect.Tray!.Number);

        var right = core.Execute("dispatch focusmonitor RIGHT").Effect;

        Assert.Equal(WorkspaceId.FromNumber(1, new MonitorId(1)), right.State.ActiveWorkspace);
        Assert.Equal("1", right.Tray!.Number);
    }

    [Fact]
    public void TheOptionDefaultsToShown()
    {
        var core = new Core();

        Assert.Equal(
            "{\"option\":\"general:tray_icon\",\"bool\":true,\"set\":false}",
            core.Execute("getoption general:tray_icon").Text);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    public void TheOptionAcceptsABool(string value, bool shown)
    {
        var core = new Core(ConfigParser.Parse($"general:tray_icon = {value}\n"));

        Assert.Empty(ConfigParser.Parse($"general:tray_icon = {value}\n").Warnings);
        Assert.Equal(shown, core.Execute("dispatch workspace 1").Effect.Tray!.Shown);
        Assert.Equal(
            "{\"option\":\"general:tray_icon\",\"bool\":" + (shown ? "true" : "false") + ",\"set\":true}",
            core.Execute("getoption general:tray_icon").Text);
    }

    [Fact]
    public void AnInvalidBoolWarnsAndKeepsThePreviousValue()
    {
        var document = ConfigParser.Parse("""
            general:tray_icon = false
            general:tray_icon = perhaps
            """);
        Assert.Equal(new ConfigWarning(2, "Invalid value 'perhaps' for general:tray_icon."), Assert.Single(document.Warnings));
        Assert.False(new Core(document).Execute("dispatch workspace 1").Effect.Tray!.Shown);

        var core = new Core();
        var reply = core.Execute("keyword general:tray_icon perhaps");

        Assert.Equal("error: Invalid value 'perhaps' for general:tray_icon.", reply.Text);
        Assert.True(reply.Effect.Tray!.Shown);
    }

    [Fact]
    public void KeywordAndReloadApplyAtOnce()
    {
        var core = new Core();
        var turnedOff = core.Execute("keyword general:tray_icon false");

        Assert.Equal("ok", turnedOff.Text);
        Assert.False(turnedOff.Effect.Tray!.Shown);
        Assert.Equal(
            "{\"option\":\"general:tray_icon\",\"bool\":false,\"set\":true}",
            core.Execute("getoption general:tray_icon").Text);

        core.ReadConfig = () => new ConfigRead(ConfigParser.Parse("general:tray_icon = true\n"));
        var reloaded = core.Execute("reload");

        Assert.Equal("ok", reloaded.Text);
        Assert.True(reloaded.Effect.Tray!.Shown);
    }

    [Fact]
    public void TheMenuIsReloadEditConfigOpenLogAndQuit()
    {
        Assert.Equal(
            [
                new TrayMenuItem(TrayCommand.Reload, "Reload"),
                new TrayMenuItem(TrayCommand.EditConfig, "Edit config"),
                new TrayMenuItem(TrayCommand.OpenLog, "Open log"),
                new TrayMenuItem(TrayCommand.Quit, "Quit"),
            ],
            TrayIcon.Menu);
    }

    [Theory]
    [InlineData("special:", "special")]
    [InlineData("special:scratch", "scratch")]
    [InlineData("12", "12")]
    public void TextUsesTheNumberOrTheName(string name, string text)
    {
        Assert.Equal(text, TrayIcon.Text(new WorkspaceId(name)));
    }

    private static Core TwoMonitors()
    {
        var core = new Core();
        core.Handle(new MonitorsChanged(
        [
            new Monitor("LEFT", "Left", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1080)),
            new Monitor("RIGHT", "Right", 1920, 0, 1920, 1080, 1, true, new Rectangle(1920, 0, 1920, 1080)),
        ]));
        core.Execute("dispatch focusmonitor LEFT");
        return core;
    }
}
