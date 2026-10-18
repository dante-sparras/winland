using Winland;

namespace Winland.Tests;

/// <summary>A window dragged onto a Group joins it, as <c>group:drag_into_group</c> and its options decide.</summary>
public class GroupDragTests
{
    private static readonly Rectangle Wide = new(0, 0, 1920, 1080);

    [Fact]
    public void ATileDroppedOnAGroupMemberJoinsTheGroup()
    {
        var core = Grouped("");

        var dropped = Drag(core, 2, Middle(Tile(core, 1)));

        Assert.Equal([new WindowId(1), new WindowId(2)], Assert.Single(dropped.State.Groups));
        Assert.Equal(new WindowId(2), dropped.Focus);
    }

    [Fact]
    public void DragIntoGroupZeroLeavesTheGroupAlone()
    {
        var core = Grouped("group:drag_into_group = 0\n");

        var dropped = Drag(core, 2, Middle(Tile(core, 1)));

        Assert.Equal([new WindowId(1)], Assert.Single(dropped.State.Groups));
    }

    [Fact]
    public void ATileDroppedOnTheFirstTabGoesInFrontOfIt()
    {
        var core = Grouped("group:drag_into_group = 2\n");
        var bar = Assert.Single(core.Handle(new Dispatch("no_op")).GroupBars);

        var dropped = Drag(core, 2, new Point(bar.Bounds.X + 2, bar.Bounds.Y + (bar.Bounds.Height / 2)));

        Assert.Equal([new WindowId(2), new WindowId(1)], Assert.Single(dropped.State.Groups));
    }

    [Fact]
    public void DragIntoGroupTwoJoinsOnlyOnTheTabBar()
    {
        var core = Grouped("group:drag_into_group = 2\n");

        var dropped = Drag(core, 2, Middle(Tile(core, 1)));

        Assert.Equal([new WindowId(1)], Assert.Single(dropped.State.Groups));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("group:merge_floated_into_tiled_on_groupbar = true\n", 2)]
    public void AFloatingWindowJoinsATiledGroupOnlyOnItsBarWithTheOption(string config, int members)
    {
        var core = Grouped(config);
        core.Handle(new Dispatch("focuswindow", "address:0x2"));
        core.Handle(new Dispatch("setfloating"));
        var bar = Assert.Single(core.Handle(new Dispatch("no_op")).GroupBars);

        var onMember = Drag(core, 2, Middle(Tile(core, 1)));
        Assert.Single(Assert.Single(onMember.State.Groups));

        var onBar = Drag(core, 2, new Point(bar.Bounds.X + 2, bar.Bounds.Y + (bar.Bounds.Height / 2)));
        Assert.Equal(members, Assert.Single(onBar.State.Groups).Count);
    }

    [Fact]
    public void ALockedGroupTakesNoDraggedWindow()
    {
        var core = Grouped("");
        core.Handle(new Dispatch("focuswindow", "address:0x1"));
        core.Handle(new Dispatch("lockactivegroup", "lock"));

        var dropped = Drag(core, 2, Middle(Tile(core, 1)));

        Assert.Equal([new WindowId(1)], Assert.Single(dropped.State.Groups));
    }

    // Window 1 is a Group of one on the left, window 2 a tile on the right.
    private static Core Grouped(string extra)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\ngroup:auto_group = false\n"
            + "bindm = SUPER, mouse:272, movewindow\n" + extra)));
        core.Handle(new WindowAppeared(new WindowId(1), Wide));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(new WindowAppeared(new WindowId(2), Wide));
        return core;
    }

    private static Outcome Drag(Core core, long window, Point to)
    {
        var from = Middle(Tile(core, window));
        core.Handle(new KeyPressed("Super_L"));
        core.Handle(new PointerButtonPressed("mouse:272") { Position = from });
        core.Handle(new PointerMoved(to));
        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = to });
        core.Handle(new KeyReleased("Super_L"));
        return dropped;
    }

    private static Rectangle Tile(Core core, long window) =>
        Assert.Single(core.Handle(new Dispatch("no_op")).State.Clients, client => client.Window == new WindowId(window)).Bounds;

    private static Point Middle(Rectangle box) => new(box.X + (box.Width / 2), box.Y + (box.Height / 2));
}
