using Winland;

namespace Winland.Tests;

public class MiddleButtonEmulationTests
{
    private const EmulatedButton Left = EmulatedButton.Left;
    private const EmulatedButton Right = EmulatedButton.Right;

    [Fact]
    public void BothButtonsPressedWithinTheWindowMakeAMiddleClick()
    {
        var emulation = new MiddleButtonEmulation();

        Assert.Equal(new MiddleStep(true), emulation.Down(Left, 100, touched: true, enabled: true));
        Assert.Equal(100 + MiddleButtonEmulation.WindowMs, emulation.ExpiresAt);
        Assert.Equal(new MiddleStep(true, MiddleDown: true), emulation.Down(Right, 120, touched: true, enabled: true));
        Assert.Null(emulation.ExpiresAt);
        Assert.Equal(new MiddleStep(true), emulation.Up(Left, 300));
        Assert.Equal(new MiddleStep(true, MiddleUp: true), emulation.Up(Right, 320));
        Assert.False(emulation.Expire(1000).Any);
    }

    [Fact]
    public void TheSecondButtonCanComeFirstAndTheLastReleaseEndsTheMiddleClick()
    {
        var emulation = new MiddleButtonEmulation();

        _ = emulation.Down(Right, 0, true, true);
        Assert.True(emulation.Down(Left, 10, true, true).MiddleDown);
        Assert.Equal(new MiddleStep(true), emulation.Up(Right, 100));
        Assert.Equal(new MiddleStep(true, MiddleUp: true), emulation.Up(Left, 110));
    }

    [Fact]
    public void AHeldPressIsSentAfterTheWindowWhenTheOtherButtonDoesNotCome()
    {
        var emulation = new MiddleButtonEmulation();
        _ = emulation.Down(Left, 100, true, true);

        Assert.False(emulation.Expire(100 + MiddleButtonEmulation.WindowMs - 1).Any);
        Assert.Equal(new MiddleStep(false, Press: Left), emulation.Expire(100 + MiddleButtonEmulation.WindowMs));
        Assert.Null(emulation.ExpiresAt);

        // The press is down now, so its release goes through and the right button is no first press.
        Assert.Equal(MiddleStep.Pass, emulation.Down(Right, 200, true, true));
        Assert.Equal(MiddleStep.Pass, emulation.Up(Right, 210));
        Assert.Equal(MiddleStep.Pass, emulation.Up(Left, 220));
    }

    [Fact]
    public void APressReleasedWithinTheWindowIsSentAsAClick()
    {
        var emulation = new MiddleButtonEmulation();
        _ = emulation.Down(Right, 100, true, true);

        Assert.Equal(new MiddleStep(true, Press: Right, Release: Right), emulation.Up(Right, 130));
        Assert.Null(emulation.ExpiresAt);
        Assert.False(emulation.Expire(1000).Any);
    }

    [Fact]
    public void AReleaseThatArrivesBeforeTheTimerFiredStillGoesAfterItsPress()
    {
        var emulation = new MiddleButtonEmulation();
        _ = emulation.Down(Left, 100, true, true);

        Assert.Equal(new MiddleStep(true, Press: Left, Release: Left), emulation.Up(Left, 100 + MiddleButtonEmulation.WindowMs + 5));
        Assert.False(emulation.Expire(1000).Any);
    }

    [Fact]
    public void ASecondPressAfterTheWindowIsNotACombination()
    {
        var emulation = new MiddleButtonEmulation();
        _ = emulation.Down(Left, 100, true, true);

        var late = emulation.Down(Right, 100 + MiddleButtonEmulation.WindowMs + 5, true, true);

        Assert.Equal(new MiddleStep(false, Press: Left), late);
        Assert.False(late.MiddleDown);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void APressWithNoFingerOnTheTouchpadOrWithTheOptionOffGoesStraightThrough(bool touched, bool enabled)
    {
        var emulation = new MiddleButtonEmulation();

        Assert.Equal(MiddleStep.Pass, emulation.Down(Left, 100, touched, enabled));
        Assert.Null(emulation.ExpiresAt);
        Assert.Equal(MiddleStep.Pass, emulation.Down(Right, 110, true, true));
        Assert.Equal(MiddleStep.Pass, emulation.Up(Left, 200));
        Assert.Equal(MiddleStep.Pass, emulation.Up(Right, 210));
    }

    [Fact]
    public void AButtonPressedWhileTheOtherIsAlreadyDownIsNotHeld()
    {
        var emulation = new MiddleButtonEmulation();
        _ = emulation.Down(Left, 0, touched: false, enabled: true);

        Assert.Equal(MiddleStep.Pass, emulation.Down(Right, 10, touched: true, enabled: true));
    }

    [Fact]
    public void ReleasesThatWereNeverHeldGoThrough()
    {
        var emulation = new MiddleButtonEmulation();

        Assert.Equal(MiddleStep.Pass, emulation.Up(Left, 0));
        Assert.Equal(MiddleStep.Pass, emulation.Up(Right, 0));
    }

    [Fact]
    public void TurningTheOptionOffWhileTheMiddleButtonIsDownStillEndsIt()
    {
        var emulation = new MiddleButtonEmulation();
        _ = emulation.Down(Left, 0, true, true);
        _ = emulation.Down(Right, 10, true, true);

        Assert.Equal(new MiddleStep(true), emulation.Down(Left, 20, true, enabled: false));
        _ = emulation.Up(Left, 30);
        Assert.True(emulation.Up(Right, 40).MiddleUp);
    }

    [Theory]
    [InlineData("middle_button_emulation = true", true)]
    [InlineData("middle_button_emulation = false", false)]
    [InlineData("middle_button_emulation = nope", false)]
    public void TheOptionIsReadIntoTheTouchpadBehavior(string line, bool emulate)
    {
        Assert.Equal(emulate, InputOptions.From(ConfigParser.Parse($"input:touchpad:{line}\n")).Touchpad.MiddleEmulation);
    }
}
