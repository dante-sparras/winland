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

    private static Layout Open(string name)
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

        layout.Apply(LayoutOptions.Unconfigured);
        layout.Fit(Area);
        return layout;
    }

    private static IReadOnlyList<Placement> Tiled(Layout layout, long window) =>
        layout.Add(Window(window), Area, null, false, new Rectangle(0, 0, 400, 300), WorkspaceId.Initial, "app", "title", monitor: Area);

    private static WindowId Window(long id) => new(id);

    private static Rectangle Bounds(IReadOnlyList<Placement> placements, long window) =>
        Assert.Single(placements, placement => placement.Client == new ClientId(window)).Bounds;
}
