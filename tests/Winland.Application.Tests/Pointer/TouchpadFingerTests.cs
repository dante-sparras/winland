namespace Winland.Application.Tests;

public class TouchpadFingerTests
{
    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(2, 100, true)]
    [InlineData(0, 0, false)]
    [InlineData(1, TouchpadPointer.StaleMs + 1, false)]
    public void ACursorMoveIsTheTouchpadsWhileAFingerIsOnIt(int fingers, long since, bool moves)
    {
        Assert.Equal(moves, TouchpadPointer.Moves(fingers, since));
    }

    [Theory]
    [InlineData(2, 0, 0, 120, true)]
    [InlineData(1, 0, 5000, 30, false)]
    [InlineData(0, 900, 900, 30, true)]
    [InlineData(0, 900, 900, 120, false)]
    [InlineData(0, 3000, TouchpadPointer.CoastMs + 1, 30, false)]
    [InlineData(2, TouchpadPointer.StaleMs + 1, 5000, 120, false)]
    public void AWheelTurnIsTheTouchpadsWhileTwoFingersScrollOrCoastAfter(int fingers, long sinceReport, long sinceTwo, int delta, bool scrolls)
    {
        Assert.Equal(scrolls, TouchpadPointer.Scrolls(fingers, sinceReport, sinceTwo, delta));
    }

    [Fact]
    public void ScaledWheelTurnsKeepWhatRoundingLeavesOnEachAxis()
    {
        var pointer = new TouchpadPointer();

        Assert.Equal(0, pointer.Scale(1, 0.4, horizontal: false));
        Assert.Equal(1, pointer.Scale(1, 0.4, horizontal: false));
        Assert.Equal(0, pointer.Scale(1, 0.4, horizontal: true));
        Assert.Equal(240, pointer.Scale(120, 2, horizontal: false));
        Assert.Equal(short.MaxValue, pointer.Scale(30000, 100, horizontal: false));
    }
}
