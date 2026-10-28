namespace Winland.Domain.Tests;

public class WindowGroupTests
{
    [Theory]
    [InlineData(0, 0, 0, 2)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(2, 2, 1, 2)]
    [InlineData(2, 0, 1, 3)]
    [InlineData(0, 2, 0, 1)]
    public void RemovingAMemberKeepsTheShownMemberOrChoosesItsPredecessor(int current, int removed, int expected, int shown)
    {
        var group = new WindowGroup(new WindowId(1));
        group.Insert(1, new WindowId(2));
        group.Insert(2, new WindowId(3));
        group.ShowAt(current);

        group.RemoveAt(removed);

        Assert.Equal(new WindowId(shown), group.Shown);
        Assert.Equal(expected, group.Current);
    }
}
