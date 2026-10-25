namespace Winland.Application.Tests;

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

    private static WindowManager Loaded(string text)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }
}
