using Winland;

namespace Winland.Tests;

public class StackedGroupBarTests
{
    // Each row: gaps_out 2 + indicator 3 + gap 0 + title 14 = 19. Three rows and the upper gap: 59.
    private static readonly GroupBarStyle Stacked = GroupBarStyle.Documented with { Stacked = true };

    private static GroupBar Bar(GroupBarStyle style, int height) =>
        new(
            new WindowId(1),
            new Rectangle(100, 50, 600, height),
            [new GroupTab(new WindowId(1), "a", true), new GroupTab(new WindowId(2), "b", false), new GroupTab(new WindowId(3), "c", false)],
            false,
            style);

    [Fact]
    public void StackedIsReadAndReservesOneRowPerTab()
    {
        Assert.True(GroupOptions.From(ConfigParser.Parse("group:groupbar:stacked = true\n")).Bar.Stacked);
        Assert.False(GroupBarStyle.Documented.Stacked);

        Assert.Equal(19, Stacked.OneBarHeight);
        Assert.Equal(59, Stacked.ReservedFor(3));
        var flat = Stacked with { Stacked = false };
        Assert.Equal(flat.Reserved, flat.ReservedFor(3));
    }

    [Fact]
    public void RowsRunFromTheFirstMemberAtTheTopToTheLastAtTheBottom()
    {
        var bar = Bar(Stacked, 59);

        Assert.Equal(600, bar.TabWidth);
        Assert.Equal(new Rectangle(0, 54, 600, 3), bar.IndicatorBounds(2));
        Assert.Equal(new Rectangle(0, 40, 600, 14), bar.TitleBounds(2));
        Assert.Equal(new Rectangle(0, 35, 600, 3), bar.IndicatorBounds(1));
        Assert.Equal(new Rectangle(0, 2, 600, 14), bar.TitleBounds(0));
        Assert.Equal(2, bar.DrawOrder(0));
        Assert.Equal(0, bar.DrawOrder(2));
    }

    [Fact]
    public void ClicksPickTheRowPastItsTopGap()
    {
        // Winland's row: ((59 - 2) - 2 * 3) / 3 = 17, plus gaps_out, is 19.
        var bar = Bar(Stacked, 59);

        Assert.Equal(-1, bar.TabAt(10, 1));
        Assert.Equal(0, bar.TabAt(10, 2));
        Assert.Equal(0, bar.TabAt(599, 18));
        Assert.Equal(-1, bar.TabAt(10, 19));
        Assert.Equal(1, bar.TabAt(10, 21));
        Assert.Equal(2, bar.TabAt(10, 56));
    }

    [Fact]
    public void DropsCountRowsInsteadOfColumns()
    {
        var bar = Bar(Stacked, 59);

        Assert.Equal(0, bar.DropIndex(new Point(300, 50)));
        Assert.Equal(1, bar.DropIndex(new Point(300, 50 + 12)));
        Assert.Equal(3, bar.DropIndex(new Point(300, 50 + 58)));

        var flat = Bar(GroupBarStyle.Documented, 21);
        Assert.Equal(0, flat.DropIndex(new Point(100 + 50, 60)));
        Assert.Equal(1, flat.DropIndex(new Point(100 + 250, 60)));
    }
}
