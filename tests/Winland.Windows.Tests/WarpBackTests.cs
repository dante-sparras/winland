using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class WarpBackTests
{
    [Fact]
    public void MouseMovesStandWhenNothingElseMovedTheCursor()
    {
        var back = new WarpBack();
        back.Configure(true);

        Assert.Null(back.Mouse(new Point(0, 0), new Point(10, 10)));
        Assert.Null(back.Mouse(new Point(10, 10), new Point(12, 10)));
    }

    [Fact]
    public void MouseMoveAfterAWarpStartsWhereTheMouseLeftTheCursor()
    {
        var back = new WarpBack();
        back.Configure(true);
        _ = back.Mouse(new Point(0, 0), new Point(100, 100));

        back.NonMouse();

        Assert.Equal(new Point(103, 98), back.Mouse(new Point(900, 500), new Point(903, 498)));
        Assert.Null(back.Mouse(new Point(103, 98), new Point(104, 98)));
    }

    [Fact]
    public void WithTheOptionOffTheWarpStands()
    {
        var back = new WarpBack();
        _ = back.Mouse(new Point(0, 0), new Point(100, 100));

        back.NonMouse();

        Assert.Null(back.Mouse(new Point(900, 500), new Point(903, 498)));
    }

    [Fact]
    public void NoMouseMoveYetLeavesNothingToGoBackTo()
    {
        var back = new WarpBack();
        back.Configure(true);
        back.NonMouse();

        Assert.Null(back.Mouse(new Point(900, 500), new Point(903, 498)));
    }

    [Fact]
    public void TheLastMousePointIsTheOneTheMoveLandedOn()
    {
        var back = new WarpBack();
        back.Configure(true);
        _ = back.Mouse(new Point(0, 0), new Point(100, 100));
        back.NonMouse();
        _ = back.Mouse(new Point(900, 500), new Point(910, 500));

        back.NonMouse();

        Assert.Equal(new Point(110, 101), back.Mouse(new Point(0, 0), new Point(0, 1)));
    }
}
