using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class CursorIdleTests
{
    [Fact]
    public void CursorHidesOnlyAfterTheIdleTimeAndReturnsOnMovement()
    {
        var clock = new ManualClock();
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, clock);
        idle.Configure(new CursorBehavior(false, 5));

        clock.Now += TimeSpan.FromSeconds(5);
        idle.Tick();
        Assert.Equal(0, cursor.Hides);

        clock.Now += TimeSpan.FromMilliseconds(1);
        idle.Tick();
        Assert.Equal(1, cursor.Hides);
        Assert.True(cursor.Hidden);

        idle.Moved();
        Assert.False(cursor.Hidden);
        Assert.Equal(1, cursor.Shows);
    }

    [Fact]
    public void KeyPressHidesUntilThePointerMoves()
    {
        var clock = new ManualClock();
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, clock);
        idle.Configure(new CursorBehavior(true, 0));

        idle.KeyPressed();
        Assert.True(cursor.Hidden);

        idle.Moved();
        Assert.False(cursor.Hidden);
    }

    [Fact]
    public void KeyPressDoesNothingWhenHidingIsOff()
    {
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, new ManualClock());
        idle.Configure(new CursorBehavior(false, 0));

        idle.KeyPressed();
        idle.Tick();

        Assert.Equal(0, cursor.Hides);
        Assert.False(cursor.Hidden);
    }

    [Fact]
    public void TurningTheTimeoutOffShowsTheCursor()
    {
        var clock = new ManualClock();
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, clock);
        idle.Configure(new CursorBehavior(false, 1));
        clock.Now += TimeSpan.FromSeconds(2);
        idle.Tick();
        Assert.True(cursor.Hidden);

        idle.Configure(new CursorBehavior(false, 0));

        Assert.False(cursor.Hidden);
    }

    [Fact]
    public void WarpMovesTheCursorAndShowsIt()
    {
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, new ManualClock());
        idle.Configure(new CursorBehavior(true, 0));
        idle.KeyPressed();

        idle.Warp(new Point(12, 34));

        Assert.Equal(new Point(12, 34), cursor.Warped);
        Assert.False(cursor.Hidden);
    }

    private sealed class ManualClock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeCursor : ICursorSurface
    {
        public int Hides { get; private set; }

        public int Shows { get; private set; }

        public bool Hidden { get; private set; }

        public Point? Warped { get; private set; }

        public void Warp(Point position) => Warped = position;

        public void Hide()
        {
            Hidden = true;
            Hides++;
        }

        public void Show()
        {
            Hidden = false;
            Shows++;
        }
    }
}
