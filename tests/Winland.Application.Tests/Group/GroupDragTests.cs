namespace Winland.Application.Tests;

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

    [Fact]
    public void AnInvadingWindowIsDroppedIntoALockedGroup()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\ngroup:auto_group = false\n"
            + "bindm = SUPER, mouse:272, movewindow\nwindowrule = group invade, class:^invade$\n")));
        core.Handle(new WindowAppeared(new WindowId(1), Wide));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(new Dispatch("lockactivegroup", "lock"));
        core.Handle(new WindowAppeared(new WindowId(2), Wide) { Traits = WindowTraits.Normal with { Class = "invade" } });

        var dropped = Drag(core, 2, Middle(Tile(core, 1)));

        Assert.Equal([new WindowId(1), new WindowId(2)], Assert.Single(dropped.State.Groups));
    }

    // Window 1 is a Group of one on the left, window 2 a tile on the right.
    [Fact]
    public void AMoveStartedOnATabTakesThatMemberOutAndDragsItAlone()
    {
        var core = Grouped("group:drag_into_group = 0\n");
        core.Handle(new Dispatch("focuswindow", "address:0x2"));
        core.Handle(new Dispatch("moveintogroup", "l"));
        var bar = Assert.Single(core.Handle(new Dispatch("no_op")).GroupBars);
        var tab = new Point(bar.Bounds.X + bar.TabWidth + bar.Style.GapsIn + 5, bar.Bounds.Y + (bar.Bounds.Height / 2));
        core.Handle(new KeyPressed("Super_L"));

        var pressed = core.Handle(new PointerButtonPressed("mouse:272") { Position = tab });
        core.Handle(new PointerMoved(new Point(1800, 900)));
        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(1800, 900) });

        Assert.Equal([new WindowId(1)], Assert.Single(pressed.State.Groups));
        Assert.Contains(pressed.Events, item => item is ClientOutOfGroup left && left.Window == new WindowId(2));
        Assert.Equal([new WindowId(1)], Assert.Single(dropped.State.Groups));
        Assert.False(Assert.Single(dropped.State.Clients, client => client.Window == new WindowId(2)).Floating);
    }

    [Fact]
    public void AMoveStartedOnTheTabOfAGroupOfOneDragsTheWholeGroup()
    {
        var core = Grouped("");
        var bar = Assert.Single(core.Handle(new Dispatch("no_op")).GroupBars);
        core.Handle(new KeyPressed("Super_L"));

        var pressed = core.Handle(new PointerButtonPressed("mouse:272") { Position = new Point(bar.Bounds.X + 5, bar.Bounds.Y + (bar.Bounds.Height / 2)) });

        Assert.Equal([new WindowId(1)], Assert.Single(pressed.State.Groups));
        Assert.DoesNotContain(pressed.Events, item => item is ClientOutOfGroup);
    }

    [Fact]
    public void ATileDraggedOntoAGroupOnAnotherMonitorJoinsItThere()
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\ngroup:auto_group = false\n"
            + "bindm = SUPER, mouse:272, movewindow\n")));
        var right = new Rectangle(1920, 0, 1920, 1080);
        core.Handle(new MonitorsChanged(
        [
            new Monitor(@"\\.\DISPLAY1", "Left", 0, 0, 1920, 1080, 1, true, Wide),
            new Monitor(@"\\.\DISPLAY2", "Right", 1920, 0, 1920, 1080, 1, true, right),
        ]));
        core.Handle(new WindowAppeared(new WindowId(1), Wide));
        core.Handle(new WindowAppeared(new WindowId(2), right));
        core.Handle(new Dispatch("focuswindow", "address:0x2"));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(new KeyPressed("Super_L"));
        var onto = Middle(Tile(core, 2));
        core.Handle(new PointerButtonPressed("mouse:272") { Position = Middle(Tile(core, 1)) });

        var moved = core.Handle(new PointerMoved(onto));
        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = onto });

        Assert.Equal(new WorkspaceId("1", 1), Client(moved, 1).Workspace);
        Assert.True(Client(moved, 1).Floating);
        Assert.Equal(new MonitorId(0), moved.State.ActiveMonitor);
        Assert.Equal([new WindowId(2)], Assert.Single(moved.State.Groups));
        Assert.Equal([@"movewindow>>1,1,\\.\DISPLAY2", @"movewindowv2>>1,1,1,\\.\DISPLAY2"], MoveLines(moved));
        Assert.Equal([new WindowId(2), new WindowId(1)], Assert.Single(dropped.State.Groups));
        Assert.Equal(new WindowId(1), dropped.Focus);
        Assert.Equal(new MonitorId(1), dropped.State.ActiveMonitor);
        Assert.False(Client(dropped, 1).Floating);
        Assert.Empty(MoveLines(dropped));
    }

    private static WindowManager Grouped(string extra)
    {
        var core = new WindowManager();
        core.Handle(new Configure(ConfigParser.Parse(
            "general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\ngroup:auto_group = false\n"
            + "bindm = SUPER, mouse:272, movewindow\n" + extra)));
        core.Handle(new WindowAppeared(new WindowId(1), Wide));
        core.Handle(new Dispatch("togglegroup"));
        core.Handle(new WindowAppeared(new WindowId(2), Wide));
        return core;
    }

    private static Outcome Drag(WindowManager core, long window, Point to)
    {
        var from = Middle(Tile(core, window));
        core.Handle(new KeyPressed("Super_L"));
        core.Handle(new PointerButtonPressed("mouse:272") { Position = from });
        core.Handle(new PointerMoved(to));
        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = to });
        core.Handle(new KeyReleased("Super_L"));
        return dropped;
    }

    private static Rectangle Tile(WindowManager core, long window) =>
        Assert.Single(core.Handle(new Dispatch("no_op")).State.Clients, client => client.Window == new WindowId(window)).Bounds;

    private static IReadOnlyList<string> MoveLines(Outcome outcome) =>
        [.. CommandProtocol.FormatStream(outcome.Events, outcome.State).Where(line =>
            line.StartsWith("movewindow>>", StringComparison.Ordinal) || line.StartsWith("movewindowv2>>", StringComparison.Ordinal))];

    private static Point Middle(Rectangle box) => new(box.X + (box.Width / 2), box.Y + (box.Height / 2));
}
