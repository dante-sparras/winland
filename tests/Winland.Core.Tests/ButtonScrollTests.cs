using Winland.Core;

namespace Winland.Core.Tests;

public class ButtonScrollTests
{
    private static ButtonScroller Scroller(bool locked = false, bool natural = false)
    {
        var scroller = new ButtonScroller();
        scroller.Configure(new ButtonScrollOptions(true, "mouse:274", locked, natural));
        return scroller;
    }

    [Fact]
    public void TheOptionsComeFromTheScrollKeys()
    {
        var options = InputOptions.From(ConfigParser.Parse("input:scroll_method = on_button_down\ninput:scroll_button = 276\ninput:scroll_button_lock = true\n"));

        Assert.Equal(new ButtonScrollOptions(true, "mouse:276", true, false), options.ButtonScroll);
        Assert.Equal("mouse:274", InputOptions.From(ConfigParser.Parse("input:scroll_method = on_button_down\n")).ButtonScroll.Button);
        Assert.False(InputOptions.From(ConfigParser.Parse("input:scroll_method = 2fg\n")).ButtonScroll.Enabled);
    }

    [Fact]
    public void HoldingTheButtonAndMovingScrolls()
    {
        var scroller = Scroller();

        Assert.Equal(ButtonScrollStep.Taken, scroller.Press("mouse:274"));
        Assert.Equal(new ButtonScrollStep(true, false, 0, -120), scroller.Move(0, 15));
        Assert.Equal(new ButtonScrollStep(true, false, 80, 0), scroller.Move(10, 0));
        Assert.Equal(ButtonScrollStep.Taken, scroller.Release("mouse:274"));
        Assert.Equal(ButtonScrollStep.Pass, scroller.Move(0, 15));
    }

    [Fact]
    public void AClickWithNoMotionIsGivenBack()
    {
        var scroller = Scroller();

        scroller.Press("mouse:274");

        Assert.Equal(new ButtonScrollStep(true, true, 0, 0), scroller.Release("mouse:274"));
        Assert.Equal(ButtonScrollStep.Pass, scroller.Press("mouse:272"));
    }

    [Fact]
    public void TheLockTurnsScrollingOnUntilTheNextClick()
    {
        var scroller = Scroller(locked: true);

        scroller.Press("mouse:274");
        Assert.Equal(ButtonScrollStep.Taken, scroller.Release("mouse:274"));
        Assert.Equal(new ButtonScrollStep(true, false, 0, -120), scroller.Move(0, 15));

        scroller.Press("mouse:274");
        scroller.Release("mouse:274");
        Assert.Equal(ButtonScrollStep.Pass, scroller.Move(0, 15));
    }

    [Fact]
    public void NaturalScrollTurnsTheOtherWay()
    {
        var scroller = Scroller(natural: true);
        scroller.Press("mouse:274");

        Assert.Equal(new ButtonScrollStep(true, false, -120, 120), scroller.Move(15, 15));
    }
}
