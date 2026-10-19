using Winland;

namespace Winland.Tests;

public class DpmsFadeTests
{
    private static readonly Timing Fade = new(Bezier.Linear, 100);

    [Fact]
    public void TurningOffFadesToBlackThenTurnsTheDisplaysOff()
    {
        var fader = new DpmsFader();

        Assert.Equal(new DpmsFadeStep(0, false, false, DpmsFader.FrameMilliseconds), fader.TurnOff(Fade, 1000));
        Assert.Equal(0.5, fader.Tick(1050).Alpha);
        Assert.Equal(new DpmsFadeStep(1, true, false, DpmsFader.WatchMilliseconds), fader.Tick(1100));
        Assert.True(fader.Off);

        // A second dpms off changes nothing.
        Assert.Equal(new DpmsFadeStep(1, false, false, DpmsFader.WatchMilliseconds), fader.TurnOff(Fade, 1200));
    }

    [Fact]
    public void TurningOnWaitsForTheDisplaysThenFadesFromBlack()
    {
        var fader = Dark();

        Assert.Equal(new DpmsFadeStep(1, false, true, DpmsFader.WatchMilliseconds), fader.TurnOn(Fade, 2000));
        Assert.Equal(new DpmsFadeStep(1, false, false, DpmsFader.FrameMilliseconds), fader.DisplaysShown(2300));
        Assert.Equal(0.5, fader.Tick(2350).Alpha);
        Assert.Equal(new DpmsFadeStep(null, false, false, null), fader.Tick(2400));
        Assert.False(fader.Showing);
    }

    [Fact]
    public void InputThatWokeTheDisplaysClearsTheBlackEvenWithoutWord()
    {
        var fader = Dark();

        fader.InputSeen(5000);
        Assert.False(fader.Off);
        Assert.Equal(1, fader.Tick(5000 + DpmsFader.WakeMilliseconds - 1).Alpha);
        Assert.Equal(1, fader.Tick(5000 + DpmsFader.WakeMilliseconds).Alpha);
        Assert.Null(fader.Tick(5000 + DpmsFader.WakeMilliseconds + 100).Alpha);
    }

    [Fact]
    public void TurningOnWhileFadingOutGoesBackFromWhereItGot()
    {
        var fader = new DpmsFader();
        fader.TurnOff(Fade, 0);
        fader.Tick(60);

        var back = fader.TurnOn(Fade, 60);
        Assert.True(back.PowerOn);
        Assert.Equal(0.6, back.Alpha!.Value, 6);
        Assert.Equal(0.3, fader.Tick(110).Alpha!.Value, 6);
        Assert.False(fader.Tick(160).PowerOff);
        Assert.False(fader.Showing);
    }

    [Fact]
    public void WithoutAFadeTheDisplaysTurnAtOnce()
    {
        var fader = Dark();

        Assert.Equal(new DpmsFadeStep(null, false, true, null), fader.TurnOn(null, 0));
        Assert.Equal(new DpmsFadeStep(null, true, false, null), fader.TurnOff(null, 0));
        Assert.Equal(new DpmsFadeStep(null, false, true, null), fader.TurnOn(Fade, 0));
    }

    [Fact]
    public void DpmsCarriesTheFadeDpmsTiming()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("animation = fadeDpms, 1, 3, default" + Environment.NewLine)));
        var power = Assert.Single(core.Handle(new Dispatch("dpms", "off")).DisplayPower);
        Assert.Equal(300, power.Fade?.Milliseconds);

        var still = new Core();
        still.Handle(new Configure(ConfigParser.Parse("animation = fadeDpms, 0" + Environment.NewLine)));
        Assert.Null(Assert.Single(still.Handle(new Dispatch("dpms", "off")).DisplayPower).Fade);
    }

    private static DpmsFader Dark()
    {
        var fader = new DpmsFader();
        fader.TurnOff(Fade, 0);
        fader.Tick(100);
        return fader;
    }
}
