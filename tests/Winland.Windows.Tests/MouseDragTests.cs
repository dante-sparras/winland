namespace Winland.Windows.Tests;

public class MouseDragTests
{
    [Fact]
    public void TheDragEndsOnlyWhenItsOwnButtonComesUp()
    {
        var drag = new MouseDrag();
        drag.Begin("mouse:272", "movewindow", "");

        Assert.Null(drag.Release("mouse:273"));
        Assert.Equal(new MouseDrag.Active("mouse:272", "movewindow", ""), drag.Release("mouse:272"));
        Assert.Null(drag.Current);
    }

    [Fact]
    public void AnAbandonedDragIsHandedBackOnceSoItsEndCanBePosted()
    {
        var drag = new MouseDrag();
        drag.Begin("mouse:273", "resizewindow", "1");

        Assert.Equal(new MouseDrag.Active("mouse:273", "resizewindow", "1"), drag.Abandon());
        Assert.Null(drag.Current);
        Assert.Null(drag.Abandon());
        Assert.Null(drag.Release("mouse:273"));
    }

    [Fact]
    public void NoDragHasNothingToAbandon() => Assert.Null(new MouseDrag().Abandon());
}
