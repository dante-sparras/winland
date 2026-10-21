using Winland.Core;

namespace Winland.Core.Tests;

public class ClickKillTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void TheNextClickKillsTheProgramUnderIt()
    {
        var core = new WindowManager();
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        core.Handle(new WindowAppeared(new WindowId(7), Area) { ProcessId = 42 });
        Assert.False(core.ClickKillArmed);

        Assert.Equal("ok", core.Execute("kill").Text);
        Assert.True(core.ClickKillArmed);

        var killed = core.Handle(new ClickKillChosen(new Point(500, 500)));

        Assert.False(core.ClickKillArmed);
        Assert.Equal(new KillRequest(42) { Window = new WindowId(7) }, killed.Kill);
        Assert.Contains("kill>>7", CommandProtocol.FormatStream(killed.Events, killed.State));
    }

    [Fact]
    public void EscapeOrAClickOnNothingKillsNothing()
    {
        var core = new WindowManager();
        core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, Area)]));
        core.Handle(new WindowAppeared(new WindowId(7), new Rectangle(0, 0, 100, 100)) { ProcessId = 42 });
        core.Execute("dispatch setfloating");

        core.Execute("kill");
        Assert.Null(core.Handle(new ClickKillChosen(null)).Kill);
        Assert.False(core.ClickKillArmed);

        core.Execute("kill");
        Assert.Null(core.Handle(new ClickKillChosen(new Point(1800, 1000))).Kill);
        Assert.False(core.ClickKillArmed);
    }
}
