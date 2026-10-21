using Winland.Windows;
using Winland.Core;

namespace Winland.Windows.Tests;

public class SizingGrabTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(17)]
    public void AWindowThatNamesASizingBorderIsSized(int hit)
    {
        Assert.Equal(DragKind.Size, SizingGrab.Classify(hit));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(18)]
    public void AWindowThatNamesItsTitleOrAButtonIsMoved(int hit)
    {
        Assert.Equal(DragKind.Move, SizingGrab.Classify(hit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AWindowWhoseEdgeLeftThePointerLeavesItToTheWindow(int hit)
    {
        Assert.Equal(DragKind.Unclear, SizingGrab.Classify(hit));
    }

    [Fact]
    public void AWindowThatDoesNotAnswerInTimeLeavesItToTheWindow()
    {
        Assert.Equal(DragKind.Unclear, SizingGrab.Classify(null));
    }

    [Fact]
    public void AChangeInSizeIsASizingEvenWhenThePositionChangesToo()
    {
        var start = new Rectangle(100, 100, 800, 600);

        Assert.Equal(DragKind.Size, SizingGrab.Resolve(start, start with { Width = 780 }));
        Assert.Equal(DragKind.Size, SizingGrab.Resolve(start, start with { Height = 620 }));
        Assert.Equal(DragKind.Size, SizingGrab.Resolve(start, new Rectangle(110, 100, 790, 600)));
    }

    [Fact]
    public void AChangeInPositionAloneIsAMove()
    {
        var start = new Rectangle(100, 100, 800, 600);

        Assert.Equal(DragKind.Move, SizingGrab.Resolve(start, start with { X = 130 }));
        Assert.Equal(DragKind.Move, SizingGrab.Resolve(start, start with { Y = 90 }));
    }

    [Fact]
    public void NoChangeLeavesTheDragUnclear()
    {
        var start = new Rectangle(100, 100, 800, 600);

        Assert.Equal(DragKind.Unclear, SizingGrab.Resolve(start, start));
    }
}
