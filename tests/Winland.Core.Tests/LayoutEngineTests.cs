using Winland;

namespace Winland.Tests;

/// <summary>Drives each Layout directly, without Core, for the paths Core cannot easily reach.</summary>
public class LayoutEngineTests
{
    private static readonly Rectangle Area = new(0, 0, 1000, 800);

    public static TheoryData<string> DraggingLayouts => new() { "dwindle", "master", "scrolling" };

    [Theory]
    [MemberData(nameof(DraggingLayouts))]
    public void ClosingALiftedTileLeavesNoHoleAndNoGhost(string name)
    {
        var layout = Open(name);
        Tiled(layout, 1);
        Tiled(layout, 2);
        Tiled(layout, 3);

        layout.Drag(Window(1), new PointerDrag(false, PointerDragPhase.Begin, new Point(100, 100)));
        layout.Drag(Window(1), new PointerDrag(false, PointerDragPhase.Move, new Point(160, 140)));
        layout.Remove(Window(1));
        var removed = layout.Placed();
        var dropped = layout.Drag(Window(1), new PointerDrag(false, PointerDragPhase.End, new Point(160, 140)));

        Assert.DoesNotContain(layout.Clients, client => client.Window == Window(1));
        Assert.DoesNotContain(dropped, placement => placement.Client == new ClientId(1));
        Assert.Equal(removed, layout.Placed());
        Assert.Equal(2, layout.Clients.Count);
    }

    [Theory]
    [MemberData(nameof(DraggingLayouts))]
    public void ClosingAGrabbedTileBeforeItLiftsEndsTheDrag(string name)
    {
        var layout = Open(name);
        Tiled(layout, 1);
        Tiled(layout, 2);

        layout.Drag(Window(1), new PointerDrag(false, PointerDragPhase.Begin, new Point(100, 100)));
        var removed = layout.Remove(Window(1));
        var moved = layout.Drag(Window(1), new PointerDrag(false, PointerDragPhase.Move, new Point(160, 140)));
        var dropped = layout.Drag(Window(1), new PointerDrag(false, PointerDragPhase.End, new Point(160, 140)));

        Assert.Empty(moved);
        Assert.DoesNotContain(dropped, placement => placement.Client == new ClientId(1));
        Assert.Equal(Area, Bounds(layout.Placed(), 2));
        Assert.Equal(Area, Bounds(removed, 2));
        Assert.Single(layout.Clients);
    }

    [Fact]
    public void DwindleHonorsAMinimumInsideASplitWhoseParentDoesNotChange()
    {
        var layout = Open("dwindle");
        Tiled(layout, 1);
        Tiled(layout, 2);

        var placements = layout.Add(Window(3), Area, null, false, new Rectangle(0, 0, 400, 300), WorkspaceId.Initial, monitor: Area, minSize: new Size(100, 600));

        Assert.Equal(new Rectangle(0, 0, 500, 800), Bounds(placements, 1));
        Assert.Equal(new Rectangle(500, 0, 500, 200), Bounds(placements, 2));
        Assert.Equal(new Rectangle(500, 200, 500, 600), Bounds(placements, 3));
    }

    [Fact]
    public void DwindleHonorsANestedMinimumThatArrivesLater()
    {
        var layout = Open("dwindle");
        Tiled(layout, 1);
        Tiled(layout, 2);
        Tiled(layout, 3);

        var placements = layout.UpdateLimits(Window(2), new Size(100, 700), null);

        Assert.Equal(new Rectangle(0, 0, 500, 800), Bounds(placements, 1));
        Assert.Equal(new Rectangle(500, 0, 500, 700), Bounds(placements, 2));
        Assert.Equal(new Rectangle(500, 700, 500, 100), Bounds(placements, 3));
    }

    [Theory]
    [InlineData("0 50")]
    [InlineData("0 -50")]
    public void ScrollingResizesARowInAColumnOfElevenWithoutThrowing(string delta)
    {
        var layout = Open("scrolling");
        for (var window = 1; window <= 11; window++)
            Tiled(layout, window);
        for (var step = 0; step < 10; step++)
            layout.LayoutMessage(Window(1), "consume");
        var before = Bounds(layout.Placed(), 1);

        var placements = layout.ResizeActive(Window(1), delta);

        Assert.Equal(before.Height, Bounds(placements, 1).Height);
        Assert.Equal(11, layout.Placed().Count);
    }

    [Fact]
    public void MasterSmartResizeNextToALargeMinimumDoesNotThrow()
    {
        var layout = Open("master");
        for (var window = 1; window <= 4; window++)
            Tiled(layout, window);
        var stack = Stack(layout.Placed());
        layout.UpdateLimits(stack[0].Window, new Size(1, 700), null);
        stack = Stack(layout.Placed());
        var middle = stack[1];

        var placements = layout.ResizeActive(middle.Window, "0 10");

        Assert.Equal(middle.Bounds.Height, Bounds(placements, middle.Window.Value).Height);
        Assert.Equal(4, placements.Count);
    }

    [Theory]
    [InlineData(1028, 1032)]
    [InlineData(1074, 1080)]
    [InlineData(988, 988)]
    public void FloatingSnapMeetsTheTaskbarOrTheMonitorBottomNotAboveIt(int bottom, int snapped)
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var work = new Rectangle(0, 0, 1920, 1032);
        var box = new Rectangle(100, bottom - 400, 400, 400);

        var placed = FloatingSnap.Apply(box, SnapEdges.All, resize: false, [], work, monitor, MonitorSnap);

        Assert.Equal(snapped, placed.Y + placed.Height);
        Assert.Equal(400, placed.Height);
    }

    [Theory]
    [InlineData(1868, 1872)]
    [InlineData(1914, 1920)]
    [InlineData(1828, 1828)]
    public void FloatingSnapMeetsAReservedRightEdgeOrTheMonitorEdge(int right, int snapped)
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);
        var work = new Rectangle(0, 0, 1872, 1080);
        var box = new Rectangle(right - 400, 300, 400, 400);

        var placed = FloatingSnap.Apply(box, SnapEdges.All, resize: true, [], work, monitor, MonitorSnap);

        Assert.Equal(snapped, placed.X + placed.Width);
        Assert.Equal(right - 400, placed.X);
    }

    [Theory]
    [MemberData(nameof(DraggingLayouts))]
    public void ExactResizeSizesTheWindowNotItsTile(string name)
    {
        var layout = Open(name, LayoutOptions.Unconfigured with { GapsIn = EdgeInsets.Uniform(5), BorderSize = 1 });
        Tiled(layout, 1);
        Tiled(layout, 2);

        var placements = layout.ResizeActive(Window(1), "exact 800 600");

        Assert.Equal(800, Bounds(placements, 1).Width);
    }

    [Fact]
    public void ScrollingKeyboardResizeKeepsTheCamera()
    {
        var layout = ThreeColumns();

        var placements = layout.ResizeActive(Window(1), "100 0");

        Assert.Equal(new Rectangle(-500, 0, 600, 800), Bounds(placements, 1));
        Assert.Equal(new Rectangle(100, 0, 500, 800), Bounds(placements, 2));
    }

    [Fact]
    public void ScrollingKeyboardResizeKeepsTheColumnInsideTheViewport()
    {
        var layout = ThreeColumns();

        var placements = layout.ResizeActive(Window(3), "200 0");

        Assert.Equal(new Rectangle(500, 0, 500, 800), Bounds(placements, 3));
    }

    [Fact]
    public void ScrollingLeftEdgeDragKeepsTheRightEdge()
    {
        var layout = ThreeColumns();

        layout.Drag(Window(3), new PointerDrag(true, PointerDragPhase.Begin, new Point(520, 100)));
        var placements = layout.Drag(Window(3), new PointerDrag(true, PointerDragPhase.Move, new Point(420, 100)));
        layout.Drag(Window(3), new PointerDrag(true, PointerDragPhase.End, new Point(420, 100)));

        Assert.Equal(new Rectangle(400, 0, 600, 800), Bounds(placements, 3));
        Assert.Equal(new Rectangle(400, 0, 600, 800), Bounds(layout.Placed(), 3));
    }

    [Fact]
    public void ScrollingKeyboardRowResizeTakesFromThePreviousRow()
    {
        var layout = OneColumnOfThree();
        var before = layout.Placed();

        var placements = layout.ResizeActive(Window(2), "0 90");

        Assert.Equal(Bounds(before, 1).Height - 90, Bounds(placements, 1).Height);
        Assert.Equal(Bounds(before, 2).Y - 90, Bounds(placements, 2).Y);
        Assert.Equal(Bounds(before, 3), Bounds(placements, 3));
    }

    [Fact]
    public void ScrollingMouseRowResizeMovesTheGrabbedEdge()
    {
        var layout = OneColumnOfThree();
        var before = layout.Placed();
        var middle = Bounds(before, 2);

        layout.Drag(Window(2), new PointerDrag(true, PointerDragPhase.Begin, new Point(middle.X + 10, middle.Y + 10)));
        var placements = layout.Drag(Window(2), new PointerDrag(true, PointerDragPhase.Move, new Point(middle.X + 10, middle.Y + 60)));

        Assert.Equal(middle.Y + 50, Bounds(placements, 2).Y);
        Assert.Equal(Bounds(before, 3), Bounds(placements, 3));
    }

    [Theory]
    [InlineData("dwindle", false)]
    [InlineData("master", false)]
    [InlineData("scrolling", false)]
    [InlineData("monocle", false)]
    [InlineData("dwindle", true)]
    [InlineData("master", true)]
    [InlineData("scrolling", true)]
    [InlineData("monocle", true)]
    public void AnAddedClientStartsWithItsClassAndTitleAsTheInitialOnes(string name, bool floating)
    {
        var layout = Open(name);

        layout.Add(Window(1), Area, null, floating, new Rectangle(0, 0, 400, 300), WorkspaceId.Initial, "app", "title", monitor: Area);

        var client = Assert.Single(layout.Clients);
        Assert.Equal("app", client.InitialClass);
        Assert.Equal("title", client.InitialTitle);
    }

    private static Layout ThreeColumns()
    {
        var layout = Open("scrolling");
        Tiled(layout, 1);
        Tiled(layout, 2);
        var placements = Tiled(layout, 3);
        Assert.Equal(new Rectangle(500, 0, 500, 800), Bounds(placements, 3));
        return layout;
    }

    private static Layout OneColumnOfThree()
    {
        var layout = Open("scrolling");
        Tiled(layout, 1);
        Tiled(layout, 2);
        Tiled(layout, 3);
        layout.LayoutMessage(Window(1), "consume");
        layout.LayoutMessage(Window(1), "consume");
        Assert.Equal(3, layout.Placed().Count);
        return layout;
    }

    private static LayoutOptions MonitorSnap { get; } = LayoutOptions.Unconfigured with
    {
        SnapEnabled = true,
        SnapWindowGap = 0,
        SnapMonitorGap = 10,
    };

    private static List<(WindowId Window, Rectangle Bounds)> Stack(IReadOnlyList<Placement> placements)
    {
        var master = placements.Min(placement => placement.Bounds.X);
        return placements
            .Where(placement => placement.Bounds.X != master)
            .OrderBy(placement => placement.Bounds.Y)
            .Select(placement => (new WindowId(placement.Client.Value), placement.Bounds))
            .ToList();
    }

    private static Layout Open(string name) => Open(name, LayoutOptions.Unconfigured);

    private static Layout Open(string name, LayoutOptions options)
    {
        Layout layout = name switch
        {
            "dwindle" => new DwindleLayout(),
            "master" => new MasterLayout(),
            "scrolling" => new ScrollingLayout(),
            _ => new MonocleLayout(),
        };
        if (layout is ScrollingLayout)
            layout.Configure(ScrollingOptions.Defaults with { FullscreenOnOneColumn = true });

        layout.Apply(options);
        layout.Fit(Area);
        return layout;
    }

    private static IReadOnlyList<Placement> Tiled(Layout layout, long window) =>
        layout.Add(Window(window), Area, null, false, new Rectangle(0, 0, 400, 300), WorkspaceId.Initial, "app", "title", monitor: Area);

    private static WindowId Window(long id) => new(id);

    private static Rectangle Bounds(IReadOnlyList<Placement> placements, long window) =>
        Assert.Single(placements, placement => placement.Client == new ClientId(window)).Bounds;
}
