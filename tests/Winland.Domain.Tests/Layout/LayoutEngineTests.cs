
namespace Winland.Domain.Tests;

/// <summary>Drives each Layout directly, without WindowManager, for the paths WindowManager cannot easily reach.</summary>
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
    public void ScrollingMovesTheCameraAgainAfterTheResizedTileIsHandedOver()
    {
        var layout = Open("scrolling");
        layout.Configure(ScrollingOptions.Defaults with { FullscreenOnOneColumn = false });
        Tiled(layout, 1);
        Tiled(layout, 2);

        layout.Drag(Window(2), new PointerDrag(true, PointerDragPhase.Begin, new Point(700, 400)));
        layout.Drag(Window(2), new PointerDrag(true, PointerDragPhase.Move, new Point(720, 400)));
        Assert.True(layout.Substitute(Window(2), Window(9), new Taken(false, false, default, "nine", "nine"), out _));
        layout.Remove(Window(1));

        // A lone column narrower than the work area is centered, which a frozen camera would not do.
        var lone = Bounds(layout.Placed(), 9);
        Assert.Equal((Area.Width - lone.Width) / 2, lone.X);
    }

    [Theory]
    [InlineData("dwindle", "splitratio nan exact")]
    [InlineData("dwindle", "splitratio infinity")]
    [InlineData("master", "mfact exact nan")]
    [InlineData("master", "mfact -infinity")]
    [InlineData("scrolling", "colresize nan")]
    [InlineData("scrolling", "colresize +nan")]
    public void ALayoutMessageWithANumberThatIsNotFiniteChangesNothing(string name, string message)
    {
        var layout = Open(name);
        Tiled(layout, 1);
        Tiled(layout, 2);
        var before = layout.Placed();

        layout.LayoutMessage(Window(2), message);

        Assert.Equal(before, layout.Placed());
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

        // A minimum leaves every other row at least a fifth of an even share, so each keeps a height.
        Assert.Equal(4, placements.Count);
        Assert.All(Stack(placements), tile => Assert.True(tile.Bounds.Height > 0, $"{tile.Window} is {tile.Bounds}"));
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

    [Theory]
    [InlineData("dwindle")]
    [InlineData("master")]
    [InlineData("scrolling")]
    [InlineData("monocle")]
    public void FloatingAgainRestoresTheLastFloatingSize(string name)
    {
        var layout = Open(name);
        Tiled(layout, 1);
        Tiled(layout, 2);

        var first = Bounds(layout.ToggleFloating(Window(1)), 1);
        layout.ResizeActive(Window(1), "exact 500 350");
        layout.ToggleFloating(Window(1));
        var again = Bounds(layout.ToggleFloating(Window(1)), 1);

        Assert.Equal((400, 300), (first.Width, first.Height));
        Assert.Equal((500, 350), (again.Width, again.Height));
    }

    [Theory]
    [InlineData("dwindle")]
    [InlineData("master")]
    [InlineData("scrolling")]
    [InlineData("monocle")]
    public void FloatingClientsKeepToTheirSizeLimits(string name)
    {
        var layout = Open(name);

        var opened = layout.Add(Window(1), Area, null, true, new Rectangle(10, 20, 400, 300), WorkspaceId.Initial, monitor: Area, minSize: new Size(500, 350));
        var limited = layout.UpdateLimits(Window(1), null, new Size(300, 200));

        Assert.Equal(new Rectangle(10, 20, 500, 350), Bounds(opened, 1));
        Assert.Equal(new Rectangle(10, 20, 300, 200), Bounds(limited, 1));
    }

    [Theory]
    [InlineData("dwindle", false)]
    [InlineData("master", false)]
    [InlineData("scrolling", false)]
    [InlineData("dwindle", true)]
    [InlineData("master", true)]
    [InlineData("scrolling", true)]
    public void AFloatingDragIgnoresAClientTiledMidDrag(string name, bool resize)
    {
        var layout = Open(name);
        Tiled(layout, 1);
        Tiled(layout, 2);
        var floated = Bounds(layout.ToggleFloating(Window(1)), 1);
        var inside = new Point(floated.X + 10, floated.Y + 10);
        layout.Drag(Window(1), new PointerDrag(resize, PointerDragPhase.Begin, inside));
        layout.ToggleFloating(Window(1));
        var tiled = layout.Placed();

        var moved = layout.Drag(Window(1), new PointerDrag(resize, PointerDragPhase.Move, new Point(inside.X + 40, inside.Y + 30)));
        layout.Drag(Window(1), new PointerDrag(resize, PointerDragPhase.End, new Point(inside.X + 40, inside.Y + 30)));

        Assert.Empty(moved);
        Assert.Equal(tiled, layout.Placed());
        Assert.False(Assert.Single(layout.Clients, client => client.Window == Window(1)).Floating);
        Assert.True(layout.TryBounds(Window(1), out var bounds));
        Assert.Equal(Bounds(tiled, 1), bounds);
    }

    [Fact]
    public void MonocleMovesAFloatingClientToTheFloatingWorkArea()
    {
        var layout = Open("monocle", LayoutOptions.Unconfigured with { FloatGaps = EdgeInsets.Uniform(20) });
        layout.Add(Window(1), Area, null, true, new Rectangle(300, 300, 200, 100), WorkspaceId.Initial, monitor: Area);

        var left = Bounds(layout.MoveWindow(Window(1), "l"), 1);
        var down = Bounds(layout.MoveWindow(Window(1), "d"), 1);

        Assert.Equal(20, left.X);
        Assert.Equal(Area.Height - 20 - 100, down.Y);
    }

    [Fact]
    public void MasterSnapsToTheWindowsItShowsNotToTheirTiles()
    {
        var layout = Open("master", LayoutOptions.Unconfigured with { BorderSize = 5, SnapEnabled = true, SnapMonitorGap = 0 });
        Tiled(layout, 1);
        layout.Add(Window(2), Area, null, true, new Rectangle(1003, 300, 100, 100), WorkspaceId.Initial, monitor: Area);

        layout.Drag(Window(2), new PointerDrag(false, PointerDragPhase.Begin, new Point(1050, 350)));
        var snapped = layout.Drag(Window(2), new PointerDrag(false, PointerDragPhase.Move, new Point(1050, 350)));

        Assert.Equal(new Rectangle(1005, 300, 100, 100), Bounds(snapped, 2));
    }

    [Fact]
    public void ScrollingDropsAMinimizedTileThatCloses()
    {
        var layout = Open("scrolling");
        Tiled(layout, 1);
        Tiled(layout, 2);
        layout.Minimize(Window(1));

        layout.Remove(Window(1));

        Assert.DoesNotContain(layout.Clients, client => client.Window == Window(1));
        Assert.False(layout.TryTake(Window(1), out _));
    }

    [Fact]
    public void MasterRestoresAMinimizedMasterToItsPlace()
    {
        var layout = Open("master");
        Tiled(layout, 1);
        Tiled(layout, 2);
        var before = Tiled(layout, 3);

        layout.Minimize(Window(1));
        var restored = layout.Restore(Window(1));

        Assert.Equal(Bounds(before, 1), Bounds(restored, 1));
        Assert.Equal(Bounds(before, 2), Bounds(restored, 2));
        Assert.Equal(Bounds(before, 3), Bounds(restored, 3));
    }

    [Fact]
    public void ANeighborTwoPixelsAwayDoesNotTouch()
    {
        var origin = new Rectangle(0, 0, 100, 100);

        Assert.Null(NeighborAt(origin, new Rectangle(102, 0, 100, 100)));
        Assert.Equal(Window(2), NeighborAt(origin, new Rectangle(101, 0, 100, 100)));
    }

    private static WindowId? NeighborAt(Rectangle origin, Rectangle other)
    {
        Client[] clients = [new(new ClientId(1), Window(1)), new(new ClientId(2), Window(2))];
        return DirectionalNeighbor.Find(clients, origin, Side.Right, Window(1), floating: false, (WindowId window, out Rectangle box) =>
        {
            box = window == Window(1) ? origin : other;
            return true;
        });
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
