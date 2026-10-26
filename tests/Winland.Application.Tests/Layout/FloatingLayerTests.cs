namespace Winland.Application.Tests;

public class FloatingLayerTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private static WindowManager Desk()
    {
        var core = new WindowManager(ConfigParser.Parse("windowrule = float, title:^(float.*)$\n"));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        return core;
    }

    private static void Open(WindowManager core, long window, string title) =>
        core.Handle(new WindowAppeared(new WindowId(window), new Rectangle(100, 100, 400, 300))
        {
            Traits = WindowTraits.Normal with { Class = "app", Title = title },
        });

    [Fact]
    public void FocusingATileKeepsTheFloatingClientsAboveItInTheirOrder()
    {
        var core = Desk();
        Open(core, 1, "tile");
        Open(core, 2, "float a");
        Open(core, 3, "float b");

        var focused = core.Handle(new WindowFocused(new WindowId(1)));

        Assert.Equal([new ZOrderRequest(new WindowId(2), true), new ZOrderRequest(new WindowId(3), true)], focused.ZOrder);
    }

    [Fact]
    public void AFocusedFloatingClientComesToTheTopOfTheFloatingOnes()
    {
        var core = Desk();
        Open(core, 1, "tile");
        Open(core, 2, "float a");
        Open(core, 3, "float b");

        Assert.Empty(core.Handle(new WindowFocused(new WindowId(2))).ZOrder);
        var focused = core.Handle(new WindowFocused(new WindowId(1)));

        Assert.Equal([new ZOrderRequest(new WindowId(3), true), new ZOrderRequest(new WindowId(2), true)], focused.ZOrder);
    }

    [Fact]
    public void NothingIsRaisedWhileTheActiveWindowStaysTheSame()
    {
        var core = Desk();
        Open(core, 2, "float a");
        Open(core, 1, "tile");
        core.Handle(new WindowFocused(new WindowId(1)));

        Assert.Empty(core.Handle(new WindowFocused(new WindowId(1))).ZOrder);
        Assert.Empty(core.Execute("dispatch resizewindow").Effect.ZOrder);
    }

    [Fact]
    public void TheFloatingClientsAboveATileAreTheShownOnesBottomFirst()
    {
        var core = Desk();
        Open(core, 1, "tile");
        Open(core, 2, "float a");
        Open(core, 3, "float b");
        Open(core, 4, "float c");
        core.Execute("dispatch movetoworkspacesilent 2,address:0x4");

        Assert.Equal([new WindowId(2), new WindowId(3)], core.FloatingAbove(new WindowId(1)));
        Assert.Empty(core.FloatingAbove(new WindowId(2)));
        Assert.Empty(core.FloatingAbove(new WindowId(99)));
    }

    [Fact]
    public void AFloatingClientOnAHiddenWorkspaceIsLeftAlone()
    {
        var core = Desk();
        Open(core, 2, "float a");
        core.Execute("dispatch movetoworkspacesilent 2,address:0x2");
        Open(core, 1, "tile");
        core.Handle(new WindowFocused(new WindowId(2)));

        Assert.Empty(core.Handle(new WindowFocused(new WindowId(1))).ZOrder);
    }
}
