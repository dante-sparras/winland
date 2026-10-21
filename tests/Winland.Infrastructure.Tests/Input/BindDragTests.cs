
namespace Winland.Infrastructure.Tests;

public class BindDragTests
{
    [Fact]
    public void TheDragEndsOnlyWhenItsOwnButtonComesUp()
    {
        var drag = new BindDrag();
        drag.Begin("mouse:272", "movewindow", "");

        Assert.Null(drag.Release("mouse:273"));
        Assert.Equal(new BindDrag.Active("mouse:272", "movewindow", ""), drag.Release("mouse:272"));
        Assert.Null(drag.Current);
    }

    // A second drag button pressed while a drag is held starts nothing, so the first drag still ends
    // when its own button comes up.
    [Fact]
    public void ASecondDragWhileOneIsHeldIsIgnored()
    {
        var drag = new BindDrag();
        drag.Begin("mouse:272", "movewindow", "");

        Assert.False(drag.Begin("mouse:273", "resizewindow", ""));
        Assert.Null(drag.Release("mouse:273"));
        Assert.Equal(new BindDrag.Active("mouse:272", "movewindow", ""), drag.Release("mouse:272"));
    }

    [Fact]
    public void AnAbandonedDragIsHandedBackOnceSoItsEndCanBePosted()
    {
        var drag = new BindDrag();
        drag.Begin("mouse:273", "resizewindow", "1");

        Assert.Equal(new BindDrag.Active("mouse:273", "resizewindow", "1"), drag.Abandon());
        Assert.Null(drag.Current);
        Assert.Null(drag.Abandon());
        Assert.Null(drag.Release("mouse:273"));
    }

    [Fact]
    public void NoDragHasNothingToAbandon() => Assert.Null(new BindDrag().Abandon());
}
