using Winland;

namespace Winland.Tests;

public class CycleNextTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void CycleNextWalksTheWindowsInTheOrderTheyOpened()
    {
        var core = Opened(new Core(), 3);

        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("cyclenext", "")).Focus);
        Assert.Equal(new WindowId(2), core.Handle(new Dispatch("cyclenext", "")).Focus);
        Assert.Equal(new WindowId(1), core.Handle(new Dispatch("cyclenext", "prev")).Focus);
        Assert.Equal(new WindowId(3), core.Handle(new Dispatch("cyclenext", "prev")).Focus);
    }

    [Theory]
    [InlineData("floating", 2L)]
    [InlineData("tiled", 3L)]
    [InlineData("tiled floating", 2L)]
    public void TiledOrFloatingAloneTakesOnlyThatKind(string arguments, long expected)
    {
        var core = Opened(new Core(), 4);
        core.Handle(new Dispatch("setfloating", "address:0x2"));
        core.Handle(new Dispatch("focuswindow", "address:0x1"));

        Assert.Equal(new WindowId(expected), core.Handle(new Dispatch("cyclenext", arguments)).Focus);
    }

    [Theory]
    [InlineData("tiled", "cyclenext")]
    [InlineData("prev tiled", "cycleprev")]
    public void TiledFromATileInMasterRunsTheLayoutsCycle(string arguments, string message)
    {
        var cycled = Opened(new Core(ConfigParser.Parse("general:layout = master\n")), 3);
        var messaged = Opened(new Core(ConfigParser.Parse("general:layout = master\n")), 3);

        Assert.Equal(
            messaged.Handle(new Dispatch("layoutmsg", message)).Focus,
            cycled.Handle(new Dispatch("cyclenext", arguments)).Focus);
    }

    private static Core Opened(Core core, int count)
    {
        for (var window = 1; window <= count; window++)
            core.Handle(new WindowAppeared(new WindowId(window), Area));

        return core;
    }
}
