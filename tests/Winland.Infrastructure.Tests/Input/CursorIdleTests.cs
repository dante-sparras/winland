
namespace Winland.Infrastructure.Tests;

public partial class CursorIdleTests
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
    public void InvisibleCursorStaysHiddenUntilRevealed()
    {
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, new ManualClock());
        idle.Configure(new CursorBehavior(false, 0) { Invisible = true });
        Assert.True(cursor.Hidden);

        idle.Moved();
        Assert.True(cursor.Hidden);

        idle.Configure(new CursorBehavior(false, 0));
        Assert.False(cursor.Hidden);

        idle.Configure(new CursorBehavior(false, 0) { Invisible = true });
        idle.Reveal();
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

    [Fact]
    public void WithoutATimeoutTheCursorNeverHidesForBeingStill()
    {
        var clock = new ManualClock();
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, clock);
        idle.Configure(new CursorBehavior(false, 0));

        clock.Now += TimeSpan.FromHours(1);
        idle.Tick();

        Assert.Equal(0, cursor.Hides);
    }

    [Fact]
    public void TurningHideOnKeyOffShowsTheCursor()
    {
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, new ManualClock());
        idle.Configure(new CursorBehavior(true, 0));
        idle.KeyPressed();

        idle.Configure(new CursorBehavior(false, 0));

        Assert.False(cursor.Hidden);
    }

    // Revealing on exit shows a cursor hidden both for being still and by a key.
    [Fact]
    public void RevealShowsTheCursorHoweverItWasHidden()
    {
        var clock = new ManualClock();
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, clock);
        idle.Configure(new CursorBehavior(true, 1));
        idle.KeyPressed();
        clock.Now += TimeSpan.FromSeconds(2);
        idle.Tick();

        idle.Reveal();

        Assert.False(cursor.Hidden);
        Assert.Equal(1, cursor.Hides);
        Assert.Equal(1, cursor.Shows);
    }

    // The daemon starts with a stand-in and swaps in the real cursor when it installs the system.
    [Fact]
    public void AnotherSurfaceTakesOverTheCursor()
    {
        var first = new FakeCursor();
        var second = new FakeCursor();
        var idle = new CursorIdle(first, new ManualClock());
        idle.Configure(new CursorBehavior(true, 0));

        idle.Use(second);
        idle.KeyPressed();

        Assert.False(first.Hidden);
        Assert.True(second.Hidden);
        Assert.Throws<ArgumentNullException>(() => idle.Use(null!));
        Assert.Throws<ArgumentNullException>(() => idle.Configure(null!));
        Assert.Throws<ArgumentNullException>(() => new CursorIdle(null!, new ManualClock()));
        Assert.Throws<ArgumentNullException>(() => new CursorIdle(first, null!));
        Assert.Throws<ArgumentNullException>(() => new CursorIdleTimer(null!));
    }

    [Fact]
    public void TheTimerHidesAStillCursorAndStopsWhenDisposed()
    {
        var clock = new ManualClock();
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, clock);
        idle.Configure(new CursorBehavior(false, 1));
        clock.Now += TimeSpan.FromSeconds(2);

        var timer = new CursorIdleTimer(idle);
        var deadline = Environment.TickCount64 + 10_000;
        while (!cursor.Hidden && Environment.TickCount64 < deadline)
            Thread.Sleep(20);
        var hidden = cursor.Hidden;
        timer.Dispose();
        idle.Moved();
        clock.Now += TimeSpan.FromSeconds(2);
        Thread.Sleep(600);

        Assert.True(hidden);
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
