namespace Winland.Application.Tests;

/// <summary>Layout options and changing the Layout, driven through pipe lines on a <see cref="TestDesktop"/>.</summary>
public class LayoutChangeTests
{
    private const string MonitorNotHonored = "error: Monitor settings other than a reserved area are not honored.";

    [Fact]
    public void SingleWindowAspectRatio()
    {
        var desk = new TestDesktop("layout:single_window_aspect_ratio = 1 1\n");
        desk.Spawn("a");
        desk.ExpectActive("442,22", "1036,1036");

        desk.Spawn("a");
        desk.CloseActive();
        desk.ExpectActive("442,22", "1036,1036");

        // A maximized Client does not keep the ratio.
        desk.Ok("fullscreen", "1");
        desk.ExpectActive("22,22", "1876,1036");
    }

    [Fact]
    public void CrashOnGeomUpdate()
    {
        var desk = new TestDesktop();
        desk.Spawn("a");
        desk.Spawn("a");
        desk.Spawn("a");

        Assert.Equal(MonitorNotHonored, desk.Send("keyword monitor HEADLESS-2,1920x1080@60,1000x0,1"));
        desk.Ok("movefocus", "r");
        Assert.Equal(MonitorNotHonored, desk.Send("keyword monitor HEADLESS-2,1920x1080@60,0x0,1"));
    }

    [Fact]
    public void LayoutRefreshDuringMonitorTeardown()
    {
        var desk = new TestDesktop();

        desk.AddMonitor("HEADLESS-5", TestDesktop.Second);

        Assert.Equal(MonitorNotHonored, desk.Send("keyword monitor HEADLESS-2,1920x1080@60,0x0,1"));
        Assert.Equal(MonitorNotHonored, desk.Send("keyword monitor HEADLESS-5,1920x1080@60,1920x0,1"));

        desk.Ok("focusmonitor", "HEADLESS-5");
        desk.Ok("workspace", "30");
        desk.Spawn("layout_teardown_a");
        desk.Ok("workspace", "31");
        desk.Spawn("layout_teardown_b");

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "1");

        Assert.Equal(MonitorNotHonored, desk.Send("keyword monitor HEADLESS-5,disable"));
        desk.Keyword("general:layout", "scrolling");

        Assert.Contains("HEADLESS-5", desk.Send("monitors all"), StringComparison.Ordinal);
    }

    [Fact]
    public void PosPreserve()
    {
        var desk = new TestDesktop();
        desk.Spawn("kitty");

        desk.Ok("togglefloating", "class:kitty");
        desk.Place("kitty", 420, 420, 1337, 69);
        desk.ExpectActive("420,420", "1337,69");

        desk.Ok("fullscreen", "0");
        desk.Ok("fullscreen", "0");
        Assert.Equal("1337,69", desk.ActivePlace().Size);

        desk.Ok("movewindow", "r");
        desk.ExpectActive("581,420", "1337,69");

        desk.Ok("fullscreen", "0");
        desk.Ok("fullscreen", "0");
        desk.ExpectActive("581,420", "1337,69");

        desk.Ok("workspace", "1");
        desk.Ok("pin", "enable class:kitty");
        desk.Ok("workspace", "2");
        desk.Ok("fullscreen", "0");
        desk.Ok("fullscreen", "0");
        desk.ExpectActive("581,420", "1337,69");
    }

    [Fact]
    public void FocusPreservedLayoutChange()
    {
        var desk = new TestDesktop("general:layout = dwindle\n");
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");
        desk.Spawn("kitty_C");
        desk.Spawn("kitty_D");

        desk.Ok("focuswindow", "class:kitty_C");
        Assert.Equal("ok", desk.Send("keyword general:layout scrolling"));

        Assert.Equal("kitty_C", desk.ActiveClass());
    }
}
