namespace Winland.Application.Tests;

public class LayoutRegressionTests
{
    private static readonly Rectangle Area = new(0, 0, 1000, 800);

    [Theory]
    [InlineData("dwindle")]
    [InlineData("scrolling")]
    public void FloatingResizeAndPlacementKeepTheClientsLimits(string name)
    {
        var core = Configured($"general:layout = {name}");
        core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Dialog = true, MinSize = new Size(200, 100), MaxSize = new Size(400, 300) },
            Bounds = new Rectangle(100, 100, 300, 200),
        });

        var resized = core.Handle(new Dispatch("resizeactive", "1000 1000"));
        Assert.Equal(new Size(400, 300), SizeOf(BoundsOf(resized, 1)));

        var placed = Place(core, 1, new Rectangle(100, 100, 50, 50));
        Assert.Equal(new Size(200, 100), SizeOf(Client(placed, 1).Bounds));
    }

    [Fact]
    public void PreselectKeepsItsDirectionForTheOpeningSplit()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new Dispatch("layoutmsg", "preselect u"));

        var opened = core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal(new Rectangle(0, 0, 1000, 400), BoundsOf(opened, 2));
        Assert.Equal(new Rectangle(0, 400, 1000, 400), BoundsOf(opened, 1));
    }

    private static Size SizeOf(Rectangle box) => new(box.Width, box.Height);

    [Fact]
    public void AScrollingColumnCannotCoverTheNeighbouringMonitorsClients()
    {
        var core = Configured("general:layout = scrolling");
        core.Handle(new MonitorsChanged([Display("left", 0, 1000, 800), Display("right", 1000, 1000, 800)]));
        for (var id = 1; id <= 3; id++)
            core.Handle(new WindowAppeared(new WindowId(id), Area));

        var moved = core.Handle(new Dispatch("layoutmsg", "move +100"));

        Assert.DoesNotContain(moved.Placements, item => item.Bounds.X + item.Bounds.Width > 1000);
        Assert.Contains(new WindowId(3), moved.Hide);
    }

    [Fact]
    public void ScrollingFullscreenKeepsTheWholeMonitorAfterItsWorkAreaChanges()
    {
        var core = Configured("general:layout = scrolling");
        var screen = Display("screen", "screen", 0, 0, 1000, 800, workArea: new Rectangle(0, 0, 1000, 760));
        core.Handle(new MonitorsChanged([screen]));
        core.Handle(new WindowAppeared(new WindowId(1), screen.WorkArea));
        core.Handle(new Dispatch("fullscreen"));

        var changed = core.Handle(new MonitorsChanged([screen with { WorkArea = new Rectangle(0, 0, 1000, 740) }]));

        Assert.Equal(Area, BoundsOf(changed, 1));
    }

    [Fact]
    public void ExpandingTheFirstColumnKeepsThatColumnInView()
    {
        var layout = Tape();
        layout.Add(new WindowId(4), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        layout.NoteFocus(new WindowId(1));
        layout.LayoutMessage(new WindowId(1), "colresize 0.75");

        var expanded = layout.LayoutMessage(new WindowId(1), "fit expand");

        Assert.Equal(new Rectangle(0, 0, 500, 800), Assert.Single(expanded, item => item.Client == new ClientId(1)).Bounds);
    }

    [Fact]
    public void GrowingAColumnPastTheViewportDoesNotShrinkIt()
    {
        var layout = Tape();
        layout.LayoutMessage(new WindowId(3), "move +100");
        layout.TryBounds(new WindowId(3), out var before);

        layout.ResizeActive(new WindowId(3), "100 0");
        layout.TryBounds(new WindowId(3), out var after);

        Assert.Equal(before.Width, after.Width);
    }

    [Fact]
    public void MovingAWindowLeavesTheUsersPreselectForTheNextWindow()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        core.Handle(new Dispatch("layoutmsg", "preselect r"));
        core.Handle(new Dispatch("movewindow", "l"));

        var opened = core.Handle(new WindowAppeared(new WindowId(3), Area));

        Assert.Equal(new Rectangle(250, 0, 250, 800), BoundsOf(opened, 3));
    }

    private static ScrollingLayout Tape()
    {
        var layout = new ScrollingLayout();
        for (var id = 1; id <= 3; id++)
            layout.Add(new WindowId(id), Area, null, false, default, WorkspaceId.Initial, monitor: Area);
        return layout;
    }

    [Fact]
    public void DwindleMinimumIncludesTheClientsOwnBorder()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area) { Traits = WindowTraits.Normal with { MinSize = new Size(480, 100) } });
        core.Execute("setprop active border_size 20");

        var split = core.Handle(new WindowAppeared(new WindowId(2), Area));

        Assert.Equal(new Rectangle(520, 0, 480, 800), BoundsOf(split, 2));
        Assert.Equal(new Rectangle(20, 20, 480, 760), BoundsOf(split, 1));
    }

    [Fact]
    public void DwindleAdoptionWithoutAMonitorKeepsTheFullMonitorForSizePrediction()
    {
        var layout = new DwindleLayout();
        var work = new Rectangle(0, 0, 1000, 760);
        layout.Add(new WindowId(1), work, null, true, new Rectangle(100, 100, 300, 200), WorkspaceId.Initial, monitor: Area);

        layout.Add(new WindowId(2), work, null, false, default, WorkspaceId.Initial, maxSize: new Size(1000, 770));

        Assert.True(Assert.Single(layout.Clients, client => client.Window == new WindowId(2)).Floating);
    }

    [Theory]
    [InlineData("dwindle", false)]
    [InlineData("scrolling", false)]
    [InlineData("dwindle", true)]
    [InlineData("scrolling", true)]
    public void FloatingPointerResizeKeepsTheClientsLimits(string name, bool border)
    {
        var core = Configured($"general:layout = {name}\ngeneral:resize_on_border = true");
        core.Handle(new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Dialog = true, MinSize = new Size(200, 100), MaxSize = new Size(400, 300) },
            Bounds = new Rectangle(0, 0, 300, 200),
        });
        var start = new Point(650, 500);
        if (border)
            core.Handle(new PointerButtonPressed("mouse:272") { Position = start });
        else
            core.Handle(new PointerGesture("resizewindow", new Point(640, 490), PointerDragPhase.Begin, "mouse:273"));

        var grown = border
            ? core.Handle(new PointerMoved(new Point(900, 700)))
            : core.Handle(new PointerGesture("resizewindow", new Point(900, 700), PointerDragPhase.Move, "mouse:273"));

        Assert.Equal(new Size(400, 300), SizeOf(Client(grown, 1).Bounds));
        var shrunk = border
            ? core.Handle(new PointerMoved(new Point(400, 350)))
            : core.Handle(new PointerGesture("resizewindow", new Point(400, 350), PointerDragPhase.Move, "mouse:273"));
        Assert.Equal(new Size(200, 100), SizeOf(Client(shrunk, 1).Bounds));
    }
}
