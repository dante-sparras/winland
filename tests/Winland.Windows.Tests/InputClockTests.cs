using Winland.Windows;
using Winland.Core;

namespace Winland.Windows.Tests;

// The clock is process-wide, so these run with the other tests that set process-wide state.
[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class InputClockTests
{
    // A focus event is judged by when it happened, not by when Winland got round to it.
    // A click 100 ms before the event is recent however late the event is handled.
    [Fact]
    public void AClickIsJudgedByTheEventTimeNotTheHandlingTime()
    {
        InputClock.Watch();
        InputClock.NoteClickOrKey(5_000);

        Assert.True(InputClock.Recent(5_100));
        Assert.False(InputClock.Recent(5_000 + InputClock.RecentMilliseconds + 1));
    }

    // The tick count wraps after about 49 days. A click just before the wrap is still recent just after it.
    [Fact]
    public void AClickJustBeforeTheTickCountWrapsIsStillRecent()
    {
        InputClock.Watch();
        InputClock.NoteClickOrKey(uint.MaxValue - 50);

        Assert.True(InputClock.Recent(50));
    }
}
