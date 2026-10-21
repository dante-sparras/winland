
namespace Winland.Infrastructure.Tests;

public class PointerSourceTests
{
    [Theory]
    [InlineData(0u, 0ul, PointerSource.Mouse)]
    [InlineData(1u, 0ul, PointerSource.Injected)]
    [InlineData(1u, 0xFF515780ul, PointerSource.Touch)]
    [InlineData(1u, 0xFF515700ul, PointerSource.Pen)]
    [InlineData(0u, 0xFF515701ul, PointerSource.Pen)]
    [InlineData(0u, 0xFF515600ul, PointerSource.Mouse)]
    public void TheSignatureTellsTouchAndPensApart(uint flags, ulong extra, PointerSource expected) =>
        Assert.Equal(expected, PointerSources.Of(flags, extra));
}

public partial class CursorIdleTests
{
    [Fact]
    public void TouchHidesTheCursorUntilTheMouseMoves()
    {
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, new ManualClock());
        idle.Configure(new CursorBehavior(false, 0));

        idle.Touched(pen: false);
        Assert.True(cursor.Hidden);

        idle.Moved();
        Assert.False(cursor.Hidden);
    }

    [Fact]
    public void APenHidesTheCursorOnlyWithHideOnTablet()
    {
        var cursor = new FakeCursor();
        var idle = new CursorIdle(cursor, new ManualClock());
        idle.Configure(new CursorBehavior(false, 0));

        idle.Touched(pen: true);
        Assert.False(cursor.Hidden);

        idle.Configure(new CursorBehavior(false, 0) { HideOnTablet = true, HideOnTouch = false });
        idle.Touched(pen: false);
        Assert.False(cursor.Hidden);
        idle.Touched(pen: true);
        Assert.True(cursor.Hidden);

        idle.Configure(new CursorBehavior(false, 0) { HideOnTablet = false, HideOnTouch = false });
        Assert.False(cursor.Hidden);
    }
}
