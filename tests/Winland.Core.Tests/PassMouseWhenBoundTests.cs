using Winland.Core;

namespace Winland.Core.Tests;

public class PassMouseWhenBoundTests
{
    [Fact]
    public void ABoundButtonIsSwallowedByDefault()
    {
        var core = Loaded("bind = SUPER, mouse:274, killactive\n");
        var session = new BindSession();
        core.Decide(new KeyPressed("Super_L"), session);

        var pressed = core.Decide(new PointerButtonPressed("mouse:274"), session);
        var released = core.Decide(new PointerButtonReleased("mouse:274"), session);

        Assert.True(pressed.Consumed);
        Assert.Equal("killactive", pressed.Dispatcher);
        Assert.True(released.Consumed);
    }

    [Fact]
    public void PassMouseWhenBoundRunsTheBindAndLetsTheButtonThrough()
    {
        var core = Loaded("binds:pass_mouse_when_bound = true\nbind = SUPER, mouse:274, killactive\n");
        var session = new BindSession();
        core.Decide(new KeyPressed("Super_L"), session);

        var pressed = core.Decide(new PointerButtonPressed("mouse:274"), session);
        var released = core.Decide(new PointerButtonReleased("mouse:274"), session);

        Assert.False(pressed.Consumed);
        Assert.Equal("killactive", pressed.Dispatcher);
        Assert.False(released.Consumed);
        Assert.Equal("{\"option\":\"binds:pass_mouse_when_bound\",\"bool\":true,\"set\":true}", core.Execute("getoption binds:pass_mouse_when_bound").Text);
    }

    [Fact]
    public void PassMouseWhenBoundLeavesAWheelBindSwallowed()
    {
        var core = Loaded("binds:pass_mouse_when_bound = true\nbind = SUPER, mouse_down, workspace, e+1\n");
        var session = new BindSession();
        core.Decide(new KeyPressed("Super_L"), session);

        var turned = core.Decide(new WheelTurned("mouse_down", 0), session);

        Assert.True(turned.Consumed);
        Assert.Equal("workspace", turned.Dispatcher);
    }

    private static WindowManager Loaded(string text)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }
}
