using Winland;

namespace Winland.Tests;

public class TouchpadTapDragTests
{
    private const int Lock = TouchpadTapDrag.Timeout;
    private const int Sticky = TouchpadTapDrag.Sticky;

    [Fact]
    public void ATapThenAMovingTouchPressesAndLiftingLocksTheButtonForTheTimeout()
    {
        var drag = Dragging(Lock);

        Assert.False(drag.Frame(Lock, Down(), 400).Any);
        Assert.True(drag.Holding);
        Assert.Equal(400 + TouchpadTapDrag.LockMs, drag.ReleaseAt);
        Assert.False(drag.Expire(399 + TouchpadTapDrag.LockMs).Any);
        Assert.True(drag.Expire(400 + TouchpadTapDrag.LockMs).Release);
        Assert.False(drag.Holding);
    }

    [Fact]
    public void TouchingAgainWithinTheTimeoutCarriesTheDragOn()
    {
        var drag = Dragging(Lock);
        _ = drag.Frame(Lock, Down(), 400);

        Assert.False(drag.Frame(Lock, Down((0, 5, 5)), 500).Any);
        Assert.True(drag.Holding);
        Assert.Null(drag.ReleaseAt);
        Assert.False(drag.Frame(Lock, Down(), 900).Any);
        Assert.True(drag.Expire(900 + TouchpadTapDrag.LockMs).Release);
    }

    [Fact]
    public void ALateFrameAfterTheTimeoutReleasesBeforeAnythingElse()
    {
        var drag = Dragging(Lock);
        _ = drag.Frame(Lock, Down(), 400);

        Assert.True(drag.Frame(Lock, Down((0, 5, 5)), 400 + TouchpadTapDrag.LockMs).Release);
        Assert.False(drag.Holding);
    }

    [Fact]
    public void AStickyDragStaysDownUntilTheNextTap()
    {
        var drag = Dragging(Sticky);
        _ = drag.Frame(Sticky, Down(), 400);

        Assert.Null(drag.ReleaseAt);
        Assert.False(drag.Expire(100000).Any);
        Assert.False(drag.Frame(Sticky, Down((0, 5, 5)), 5000).Any);
        Assert.False(drag.Frame(Sticky, Down((0, 8, 5)), 5050).Any);
        Assert.False(drag.Frame(Sticky, Down(), 5100).Any);
        Assert.True(drag.Holding);

        Assert.False(drag.Frame(Sticky, Down((0, 5, 5)), 6000).Any);
        Assert.True(drag.Frame(Sticky, Down(), 6100).Release);
        Assert.False(drag.Holding);
    }

    [Fact]
    public void AStickyTouchHeldLongerThanATapLeavesTheDragLocked()
    {
        var drag = Dragging(Sticky);
        _ = drag.Frame(Sticky, Down(), 400);
        _ = drag.Frame(Sticky, Down((0, 5, 5)), 1000);

        Assert.False(drag.Frame(Sticky, Down(), 1000 + TouchpadTapDrag.TapMs + 1).Any);
        Assert.True(drag.Holding);
    }

    [Fact]
    public void ASecondTouchThatLiftsWithoutMovingPressesNothing()
    {
        var drag = Tapped(Lock);

        Assert.False(drag.Frame(Lock, Down((0, 5, 5)), 200).Any);
        Assert.False(drag.Frame(Lock, Down(), 250).Any);
        Assert.False(drag.Holding);
    }

    [Fact]
    public void ASecondTouchAfterTheDragWindowStartsOver()
    {
        var drag = Tapped(Lock);

        Assert.False(drag.Frame(Lock, Down((0, 5, 5)), 60 + TouchpadTapDrag.DragWindowMs + 1).Any);
        Assert.False(drag.Frame(Lock, Down((0, 15, 5)), 500).Any);
        Assert.False(drag.Holding);
    }

    [Fact]
    public void ATouchThatMovesOrLastsTooLongIsNoTap()
    {
        var moved = new TouchpadTapDrag();
        _ = moved.Frame(Lock, Down((0, 5, 5)), 0);
        _ = moved.Frame(Lock, Down((0, 9, 5)), 20);
        _ = moved.Frame(Lock, Down(), 60);
        _ = moved.Frame(Lock, Down((0, 5, 5)), 100);
        Assert.False(moved.Frame(Lock, Down((0, 15, 5)), 120).Any);

        var slow = new TouchpadTapDrag();
        _ = slow.Frame(Lock, Down((0, 5, 5)), 0);
        _ = slow.Frame(Lock, Down(), TouchpadTapDrag.TapMs + 1);
        _ = slow.Frame(Lock, Down((0, 5, 5)), TouchpadTapDrag.TapMs + 50);
        Assert.False(slow.Frame(Lock, Down((0, 15, 5)), TouchpadTapDrag.TapMs + 70).Any);
    }

    [Fact]
    public void AnotherFingerEndsTheDragAtOnce()
    {
        var drag = Dragging(Lock);

        Assert.True(drag.Frame(Lock, Down((0, 15, 5), (1, 30, 5)), 350).Release);
        Assert.False(drag.Holding);
        Assert.False(drag.Frame(Lock, Down(), 400).Any);

        var locked = Dragging(Sticky);
        _ = locked.Frame(Sticky, Down(), 400);
        Assert.True(locked.Frame(Sticky, Down((0, 5, 5), (1, 30, 5)), 500).Release);
    }

    [Fact]
    public void TurningTheLockOffLetsTheButtonGo()
    {
        var drag = Dragging(Lock);

        Assert.True(drag.Frame(0, Down((0, 15, 5)), 350).Release);
        Assert.False(drag.Holding);
        Assert.False(drag.Frame(0, Down(), 400).Any);
    }

    [Theory]
    [InlineData("drag_lock = 1", 1)]
    [InlineData("drag_lock = 2", 2)]
    [InlineData("drag_lock = 9", 2)]
    [InlineData("drag_lock = 0", 0)]
    [InlineData("drag_lock = 1\ninput:touchpad:tap-and-drag = false", 0)]
    [InlineData("drag_lock = 1\ninput:touchpad:tap-to-click = false", 0)]
    public void TheLockModeIsCarriedOutOnlyWhileTappingAndTapAndDragAreOn(string lines, int mode)
    {
        Assert.Equal(mode, Read($"input:touchpad:{lines}\n").Touchpad.DragLock);
    }

    [Fact]
    public void WindowsOwnTapAndDragIsTurnedOffWhileTheLockIsOn()
    {
        Assert.Equal(false, Read("input:touchpad:drag_lock = 2\n").Request()?.Touchpad?.TapAndDrag);
        Assert.Equal(false, Read("input:touchpad:drag_lock = 1\ninput:touchpad:tap-and-drag = true\n").Request()?.Touchpad?.TapAndDrag);
        Assert.Null(Read("input:touchpad:drag_lock = 0\n").Request());
        Assert.Equal(true, Read("input:touchpad:tap-and-drag = true\n").Request()?.Touchpad?.TapAndDrag);
        Assert.Equal(false, Read("input:touchpad:drag_lock = 1\ninput:touchpad:tap-and-drag = false\n").Request()?.Touchpad?.TapAndDrag);
    }

    private static InputOptions Read(string text) => InputOptions.From(ConfigParser.Parse(text));

    private static TouchpadTapDrag Tapped(int mode)
    {
        var drag = new TouchpadTapDrag();
        _ = drag.Frame(mode, Down((0, 5, 5)), 0);
        _ = drag.Frame(mode, Down(), 60);
        return drag;
    }

    private static TouchpadTapDrag Dragging(int mode)
    {
        var drag = Tapped(mode);
        Assert.False(drag.Frame(mode, Down((0, 5, 5)), 100).Any);
        Assert.False(drag.Frame(mode, Down((0, 6, 5)), 150).Any);
        Assert.True(drag.Frame(mode, Down((0, 9, 5)), 200).Press);
        Assert.True(drag.Holding);
        _ = drag.Frame(mode, Down((0, 15, 5)), 300);
        return drag;
    }

    private static Dictionary<int, (double X, double Y)> Down(params (int Id, double X, double Y)[] fingers) =>
        fingers.ToDictionary(f => f.Id, f => (f.X, f.Y));
}
