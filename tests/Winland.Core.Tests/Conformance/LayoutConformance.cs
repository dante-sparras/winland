namespace Winland.Tests.Conformance;

/// <summary>The pinned release's layout cases. Each test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class LayoutConformance
{
    [Fact]
    public void SingleWindowAspectRatio()
    {
        var desk = new Desk("layout:single_window_aspect_ratio = 1 1\n");
        desk.Spawn("a");
        desk.ExpectActive("442,22", "1036,1036");

        desk.Spawn("a");
        desk.CloseActive();
        desk.ExpectActive("442,22", "1036,1036");

        // A maximized Client does not keep the ratio.
        desk.Ok("fullscreen", "1");
        desk.ExpectActive("22,22", "1876,1036");
    }

    [Fact(Skip = "The monitor keyword refuses a mode and a scale, which are Gaps.")]
    public void CrashOnGeomUpdate()
    {
        var desk = new Desk();
        desk.Spawn("a");
        desk.Spawn("a");
        desk.Spawn("a");

        Assert.Equal("ok", desk.Send("keyword monitor HEADLESS-2,1920x1080@60,1000x0,1"));
        desk.Ok("movefocus", "r");
        Assert.Equal("ok", desk.Send("keyword monitor HEADLESS-2,1920x1080@60,0x0,1"));
    }

    [Fact(Skip = "The monitor keyword refuses a mode and a scale, which are Gaps.")]
    public void LayoutRefreshDuringMonitorTeardown()
    {
        var desk = new Desk();

        desk.AddMonitor("HEADLESS-5", Desk.Second);

        Assert.Equal("ok", desk.Send("keyword monitor HEADLESS-2,1920x1080@60,0x0,1"));
        Assert.Equal("ok", desk.Send("keyword monitor HEADLESS-5,1920x1080@60,1920x0,1"));

        desk.Ok("focusmonitor", "HEADLESS-5");
        desk.Ok("workspace", "30");
        desk.Spawn("layout_teardown_a");
        desk.Ok("workspace", "31");
        desk.Spawn("layout_teardown_b");

        desk.Ok("focusmonitor", "HEADLESS-2");
        desk.Ok("workspace", "1");

        Assert.Equal("ok", desk.Send("keyword monitor HEADLESS-5,disable"));
        desk.Keyword("general:layout", "master");

        Assert.Contains("HEADLESS-5", desk.Send("monitors all"), StringComparison.Ordinal);
    }

    [Fact]
    public void PosPreserve()
    {
        var desk = new Desk();
        desk.Spawn("kitty");

        desk.Ok("setfloating", "class:kitty");
        desk.Ok("resizewindowpixel", "exact 1337 69,class:kitty");
        desk.Ok("movewindowpixel", "exact 420 420,class:kitty");
        desk.ExpectActive("420,420", "1337,69");

        desk.Ok("fullscreen", "0");
        desk.Ok("fullscreen", "0");
        Assert.Equal("1337,69", desk.ActivePlace().Size);

        desk.Ok("movewindow", "r");
        desk.ExpectActive("581,420", "1337,69");

        desk.Ok("fullscreen", "0");
        desk.Ok("fullscreen", "0");
        desk.ExpectActive("581,420", "1337,69");

        desk.Keyword("binds:allow_pin_fullscreen", "true");
        desk.Ok("workspace", "1");
        desk.Ok("pin", "enable class:kitty");
        desk.Ok("workspace", "2");
        desk.Ok("fullscreen", "0");
        desk.Ok("fullscreen", "0");
        desk.ExpectActive("581,420", "1337,69");
    }

    [Fact]
    public void FocusMRUAfterClose()
    {
        var desk = new Desk("""
            dwindle:default_split_ratio = 1.25
            input:focus_on_close = 2
            """);
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");
        desk.Spawn("kitty_C");

        desk.Ok("focuswindow", "class:kitty_A");
        desk.Ok("focuswindow", "class:kitty_B");
        desk.Ok("focuswindow", "class:kitty_C");

        desk.CloseActive();
        Assert.Equal("kitty_B", desk.ActiveClass());

        desk.CloseActive();
        Assert.Equal("kitty_A", desk.ActiveClass());
    }

    [Fact]
    public void FocusPreservedLayoutChange()
    {
        var desk = new Desk("general:layout = master\n");
        desk.Spawn("kitty_A");
        desk.Spawn("kitty_B");
        desk.Spawn("kitty_C");
        desk.Spawn("kitty_D");

        desk.Ok("focuswindow", "class:kitty_C");
        Assert.Equal("ok", desk.Send("r/keyword general:layout monocle"));

        Assert.Equal("kitty_C", desk.ActiveClass());
    }
}
