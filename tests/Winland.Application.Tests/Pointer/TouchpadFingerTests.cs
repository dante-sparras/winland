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
}
