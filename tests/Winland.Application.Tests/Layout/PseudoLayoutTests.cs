namespace Winland.Application.Tests;

public class PseudoLayoutTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("dwindle")]
    [InlineData("scrolling")]
    public void PseudotileCentersTheClientsOwnSizeAndTogglesOff(string layout)
    {
        var core = Configured(Flat($"general:layout = {layout}\n"));
        core.Handle(Appeared(1, new Rectangle(0, 0, 800, 600)));

        var on = core.Handle(new Pseudo(new WindowId(1)));
        var off = core.Handle(new Pseudo(new WindowId(1)));

        Assert.Equal(new Rectangle(560, 240, 800, 600), BoundsOf(on, 1));
        Assert.Equal(Wide, BoundsOf(off, 1));
    }

    [Theory]
    [InlineData("dwindle")]
    [InlineData("scrolling")]
    public void PseudotileUsesTheDefaultSizeWhenTheClientHasNone(string layout)
    {
        var core = Configured(Flat($"general:layout = {layout}\n"));
        core.Handle(Appeared(1));

        var outcome = core.Handle(new Pseudo(new WindowId(1)));

        Assert.Equal(new Rectangle(320, 180, 1280, 720), BoundsOf(outcome, 1));
    }

    [Theory]
    [InlineData("dwindle")]
    [InlineData("scrolling")]
    public void PseudotileScalesDownToFitTheTileAndLeavesTheOther(string layout)
    {
        var core = Configured(Flat($"general:layout = {layout}\nmaster:mfact = 0.5\n"));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2, new Rectangle(0, 0, 1280, 720)));

        var outcome = core.Handle(new Pseudo(new WindowId(2)));

        Assert.Equal(new Rectangle(0, 0, 960, 1080), BoundsOf(outcome, 1));
        Assert.Equal(new Rectangle(960, 270, 960, 540), BoundsOf(outcome, 2));
    }

    [Fact]
    public void PseudotileScalesDownToFitAMonocleTile()
    {
        var core = Configured(Flat("general:layout = monocle\n"));
        core.Handle(Appeared(1, new Rectangle(0, 0, 1000, 4000)));

        var outcome = core.Handle(new Pseudo(new WindowId(1)));

        Assert.Equal(new Rectangle(825, 0, 270, 1080), BoundsOf(outcome, 1));
    }

    [Theory]
    [InlineData("dwindle")]
    [InlineData("scrolling")]
    public void PseudotileLosesItsBorderAfterItIsCentered(string layout)
    {
        var core = Configured($"""
            general:gaps_in = 0
            general:gaps_out = 0
            general:border_size = 2
            general:layout = {layout}
            """);
        core.Handle(Appeared(1, new Rectangle(0, 0, 800, 600)));

        var outcome = core.Handle(new Pseudo(new WindowId(1)));

        Assert.Equal(new Rectangle(562, 242, 796, 596), BoundsOf(outcome, 1));
    }

    private static string Flat(string extra) =>
        "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + extra;

    private static WindowAppeared Appeared(long window, Rectangle bounds = default) =>
        new(new WindowId(window), Wide) { Bounds = bounds };
}
