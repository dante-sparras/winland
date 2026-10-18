using Winland;

namespace Winland.Tests;

public class DimAroundTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);

    private static Core Desk(string config)
    {
        var core = new Core(ConfigParser.Parse(config));
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1040))]));
        return core;
    }

    private static Outcome Open(Core core, long window, string title) =>
        core.Handle(new WindowAppeared(new WindowId(window), Screen)
        {
            Traits = WindowTraits.Normal with { Class = "app", Title = title },
        });

    [Fact]
    public void ARuleDimsTheWholeMonitorBelowItsWindow()
    {
        var core = Desk("windowrulev2 = dim_around, title:^(dialog)$\n");
        Open(core, 1, "main");
        var outcome = Open(core, 2, "dialog");

        var dim = Assert.Single(outcome.Dims);
        Assert.Equal(new WindowId(2), dim.Window);
        Assert.Equal(new Rectangle(0, 0, 1920, 1080), dim.Monitor);
        Assert.Equal(0.4, dim.Alpha, 3);
    }

    [Fact]
    public void TheDimFollowsTheWindowsOpacityAndTheOption()
    {
        var core = Desk("windowrulev2 = dim_around, title:^(dialog)$\ndecoration:dim_around = 0.5\ndecoration:inactive_opacity = 0.5\n");
        var outcome = Open(core, 2, "dialog");
        Assert.Equal(0.5, Assert.Single(outcome.Dims).Alpha, 2);

        outcome = Open(core, 1, "main");
        Assert.Equal(0.25, Assert.Single(outcome.Dims).Alpha, 2);

        var off = Desk("windowrulev2 = dim_around, title:^(dialog)$\ndecoration:dim_around = 0\n");
        Assert.Empty(Open(off, 2, "dialog").Dims);
    }

    [Fact]
    public void SetpropTurnsTheDimOnAndAHiddenWindowDimsNothing()
    {
        var core = Desk("");
        Open(core, 1, "main");
        Assert.Empty(core.Execute("dispatch no_op").Effect.Dims);

        Assert.Equal("ok", core.Execute("setprop address:0x1 dim_around 1").Text);
        Assert.Single(core.Execute("dispatch no_op").Effect.Dims);

        core.Execute("dispatch movetoworkspacesilent 2,address:0x1");
        Assert.Empty(core.Execute("dispatch no_op").Effect.Dims);
    }
}
