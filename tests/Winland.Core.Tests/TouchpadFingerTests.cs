using Winland;

namespace Winland.Tests;

public class TouchpadFingerTests
{
    private const double Px = TouchpadDrag.PixelsPerMillimetre;

    [Fact]
    public void TheTouchpadKeysWindowsLacksAreReadIntoTheBehavior()
    {
        var options = InputOptions.From(ConfigParser.Parse("""
            input {
                scroll_factor = 2
                touchpad {
                    scroll_factor = 0.5
                    flip_x = true
                    drag_3fg = 2
                    tap_button_map = lmr
                }
            }
            """));

        Assert.Equal(new TouchpadBehavior(0.5, 2, true, false, 4, true), options.Touchpad);
        Assert.Equal(false, options.Request()?.Touchpad?.TwoFingerTap);
        Assert.Equal(TouchpadBehavior.Default, InputOptions.Documented.Touchpad);
    }

    [Theory]
    [InlineData("drag_3fg = 1", 3)]
    [InlineData("drag_3fg = 3_finger", 3)]
    [InlineData("drag_3fg = 4_finger", 4)]
    [InlineData("drag_3fg = 7", 4)]
    [InlineData("drag_3fg = disable", 0)]
    public void DragThreeFingersTakesItsNamesAndClampsItsNumber(string line, int fingers)
    {
        Assert.Equal(fingers, InputOptions.From(ConfigParser.Parse($"input:touchpad:{line}\n")).Touchpad.DragFingers);
    }

    [Fact]
    public void TheTapMapSetsWindowsTwoFingerTapAndOnlyLmrTapsTheMiddleButton()
    {
        Assert.Equal(true, Read("input:touchpad:tap_button_map = lrm\n").Request()?.Touchpad?.TwoFingerTap);
        Assert.False(Read("input:touchpad:tap_button_map = lrm\n").Touchpad.MiddleTap);
        Assert.False(Read("input:touchpad:tap_button_map = lmr\ninput:touchpad:tap-to-click = false\n").Touchpad.MiddleTap);
        Assert.Null(Read("input:touchpad:tap_button_map = rml\n").TouchpadTapButtonMap);
        Assert.Null(Read("input:touchpad:tap_button_map = lmr\ninput:touchpad:tap_button_map = \n").TouchpadTapButtonMap);
        Assert.Equal("lmr", Read("input:touchpad:tap_button_map = lmr\ninput:touchpad:tap_button_map = bad\n").TouchpadTapButtonMap);
    }

    [Fact]
    public void TheTouchpadScrollFactorIsClampedTo100()
    {
        Assert.Equal(100, Read("input:touchpad:scroll_factor = 400\n").TouchpadScrollFactor);
        Assert.Equal(0, Read("input:touchpad:scroll_factor = -1\n").TouchpadScrollFactor);
    }

    [Fact]
    public void TheTwoFingerTapSurvivesACrash()
    {
        var pad = new TouchpadSettings(true, null, null, null, null, false);
        var snapshot = new RecoverySnapshot([]) { Input = new InputOriginals { Touchpad = pad } };

        Assert.True(RecoverySnapshot.TryParse(snapshot.ToJson(), out var read));
        Assert.Equal(pad, read.Input?.Touchpad);
    }

    [Fact]
    public void ATouchpadWheelTurnIsScaledByItsFactorLessTheWheelLines()
    {
        Assert.Equal(1, TouchpadBehavior.Default.WheelScale(true));
        Assert.Equal(3, (TouchpadBehavior.Default with { ScrollFactor = 3 }).WheelScale(true));
        Assert.Equal(1, (TouchpadBehavior.Default with { ScrollFactor = 3 }).WheelScale(false));
        Assert.Equal(1.5, (TouchpadBehavior.Default with { ScrollFactor = 3, WheelFactor = 2 }).WheelScale(true));
        Assert.Equal(3, (TouchpadBehavior.Default with { ScrollFactor = 3, WheelFactor = 0 }).WheelScale(true));
    }

    [Fact]
    public void AWindowsOwnFactorsReplaceTheConfiguredOnes()
    {
        var pad = TouchpadBehavior.Default with { ScrollFactor = 3, WheelFactor = 2 };

        Assert.Equal(0.25, pad.WheelScale(true, new ScrollOverride(null, 0.5)));
        Assert.Equal(1.5, pad.WheelScale(true, new ScrollOverride(4, null)));
        Assert.Equal(2, pad.WheelScale(false, new ScrollOverride(4, 0.5)));
        Assert.Equal(1, pad.WheelScale(false, new ScrollOverride(null, 0.5)));
        Assert.Equal(0, pad.WheelScale(false, new ScrollOverride(0, null)));
        Assert.Equal(0.5, (pad with { ScrollFactor = 0 }).WheelScale(false, new ScrollOverride(4, 1)));
    }

    [Fact]
    public void ATouchpadScrollFactorOfZeroStopsEveryWheel()
    {
        var stopped = TouchpadBehavior.Default with { ScrollFactor = 0 };

        Assert.Equal(0, stopped.WheelScale(true));
        Assert.Equal(0, stopped.WheelScale(false));
    }

    [Fact]
    public void FlippingTurnsOnlyTheNamedAxesAround()
    {
        Assert.Equal((-3d, 4d), (TouchpadBehavior.Default with { FlipX = true }).Flip(3, 4));
        Assert.Equal((3d, -4d), (TouchpadBehavior.Default with { FlipY = true }).Flip(3, 4));
        Assert.False(TouchpadBehavior.Default.Flips);
        Assert.Equal((3d, 4d), TouchpadBehavior.Default.Flip(3, 4));
    }

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

    [Fact]
    public void ThreeFingersThatTravelPressMoveAndAfterTheLiftTimeRelease()
    {
        var drag = new TouchpadDrag();

        Assert.False(drag.Frame(3, Down((0, 10, 10), (1, 20, 10), (2, 30, 10)), 0).Any);
        var press = drag.Frame(3, Down((0, 12, 10), (1, 22, 10), (2, 32, 10)), 10);
        var move = drag.Frame(3, Down((0, 13, 9), (1, 23, 9), (2, 33, 9)), 20);
        var lift = drag.Frame(3, Down((0, 13, 9)), 30);
        var up = drag.Frame(3, Down(), 40);

        Assert.True(press.Press);
        Assert.Equal(2 * Px, press.Dx, 6);
        Assert.Equal(new TouchpadDragStep(false, Px, -Px, false).Dx, move.Dx, 6);
        Assert.Equal(-Px, move.Dy, 6);
        Assert.False(lift.Any);
        Assert.False(up.Any);
        Assert.True(drag.Holding);
        Assert.Equal(30 + TouchpadDrag.LiftMs, drag.ReleaseAt);
        Assert.False(drag.Expire(29 + TouchpadDrag.LiftMs).Any);
        Assert.True(drag.Expire(30 + TouchpadDrag.LiftMs).Release);
        Assert.False(drag.Holding);
        Assert.Null(drag.ReleaseAt);
    }

    [Fact]
    public void FingersPutBackWithinTheLiftTimeCarryTheDragOn()
    {
        var drag = Dragging(3);

        Assert.False(drag.Frame(3, Down(), 100).Any);
        Assert.False(drag.Frame(3, Down((3, 50, 50), (4, 60, 50), (5, 70, 50)), 200).Any);
        Assert.Null(drag.ReleaseAt);
        var move = drag.Frame(3, Down((3, 51, 50), (4, 61, 50), (5, 71, 50)), 210);

        Assert.Equal(Px, move.Dx, 6);
        Assert.False(drag.Expire(10_000).Any);
        Assert.True(drag.Holding);
    }

    [Fact]
    public void AFingerMoreEndsTheDragAtOnce()
    {
        var drag = Dragging(3);

        Assert.True(drag.Frame(3, Down((0, 12, 10), (1, 22, 10), (2, 32, 10), (3, 40, 10)), 50).Release);
        Assert.False(drag.Holding);

        var lifted = Dragging(3);
        _ = lifted.Frame(3, Down(), 50);
        Assert.True(lifted.Frame(3, Down((0, 1, 1), (1, 2, 1), (2, 3, 1), (3, 4, 1)), 60).Release);
    }

    [Fact]
    public void FingersThatDoNotTravelOrAreNotTheDragCountDoNothing()
    {
        var drag = new TouchpadDrag();

        Assert.False(drag.Frame(3, Down((0, 10, 10), (1, 20, 10), (2, 30, 10)), 0).Any);
        Assert.False(drag.Frame(3, Down((0, 10.5, 10), (1, 20.5, 10), (2, 30.5, 10)), 10).Any);
        Assert.False(drag.Frame(3, Down((0, 10.5, 10), (1, 20.5, 10)), 20).Any);
        Assert.False(drag.Frame(3, Down((0, 15, 10), (1, 25, 10)), 30).Any);
        Assert.False(drag.Frame(4, Down((0, 10, 10), (1, 20, 10), (2, 30, 10)), 40).Any);
        Assert.False(drag.Frame(4, Down((0, 15, 10), (1, 25, 10), (2, 35, 10)), 50).Any);
        Assert.False(drag.Frame(0, Down((0, 10, 10), (1, 20, 10), (2, 30, 10)), 60).Any);
        Assert.False(drag.Frame(0, Down((0, 15, 10), (1, 25, 10), (2, 35, 10)), 70).Any);
        Assert.False(drag.Holding);
    }

    [Fact]
    public void TurningTheDragOffLetsTheButtonGo()
    {
        var drag = Dragging(4);

        Assert.True(drag.Frame(0, Down((0, 12, 10), (1, 22, 10), (2, 32, 10), (3, 42, 10)), 50).Release);
        Assert.False(drag.Holding);
    }

    [Fact]
    public void TwoFingersThatTouchAndLiftQuicklyTap()
    {
        var tap = new TouchpadTap();

        Assert.False(tap.Frame(Down((0, 10, 10)), 0));
        Assert.False(tap.Frame(Down((0, 10, 10), (1, 20, 10)), 10));
        Assert.False(tap.Frame(Down((0, 10.5, 10), (1, 20, 10.5)), 50));
        Assert.False(tap.Frame(Down((1, 20, 10.5)), 100));
        Assert.True(tap.Frame(Down(), TouchpadTap.TimeoutMs));
        Assert.False(tap.Frame(Down(), 200));
    }

    [Fact]
    public void ASlowMovedOrThreeFingerTouchIsNoTap()
    {
        var slow = new TouchpadTap();
        _ = slow.Frame(Down((0, 10, 10), (1, 20, 10)), 0);
        Assert.False(slow.Frame(Down(), TouchpadTap.TimeoutMs + 1));

        var moved = new TouchpadTap();
        _ = moved.Frame(Down((0, 10, 10), (1, 20, 10)), 0);
        _ = moved.Frame(Down((0, 10, 10), (1, 20, 11.5)), 10);
        Assert.False(moved.Frame(Down(), 20));

        var three = new TouchpadTap();
        _ = three.Frame(Down((0, 10, 10), (1, 20, 10), (2, 30, 10)), 0);
        Assert.False(three.Frame(Down(), 20));

        var one = new TouchpadTap();
        _ = one.Frame(Down((0, 10, 10)), 0);
        Assert.False(one.Frame(Down(), 20));
    }

    private static InputOptions Read(string text) => InputOptions.From(ConfigParser.Parse(text));

    private static TouchpadDrag Dragging(int fingers)
    {
        var drag = new TouchpadDrag();
        var start = Enumerable.Range(0, fingers).Select(i => (i, 10d + (10 * i), 10d)).ToArray();
        var moved = start.Select(p => (p.i, p.Item2 + 2, p.Item3)).ToArray();
        _ = drag.Frame(fingers, Down(start), 0);
        Assert.True(drag.Frame(fingers, Down(moved), 10).Press);
        return drag;
    }

    private static Dictionary<int, (double X, double Y)> Down(params (int Id, double X, double Y)[] fingers) =>
        fingers.ToDictionary(f => f.Id, f => (f.X, f.Y));
}
