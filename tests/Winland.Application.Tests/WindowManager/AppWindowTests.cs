namespace Winland.Application.Tests;

public class AppWindowTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("dwindle")]
    [InlineData("scrolling")]
    public void AWindowAdoptedUnderADragKeepsItsFrameUntilTheButtonIsReleased(string layout)
    {
        var core = new WindowManager(ConfigParser.Parse($"general:layout = {layout}"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var bounds = new Rectangle(200, 250, 500, 350);
        var opened = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Opened = true,
            Dragging = true,
            Pointer = new Point(250, 260),
            Bounds = bounds,
        });

        var dragged = Assert.Single(opened.State.Clients, client => client.Window == new WindowId(2));
        Assert.True(dragged.Floating);
        Assert.Equal(bounds, dragged.Bounds);
        Assert.DoesNotContain(opened.Placements, placement => placement.Client == dragged.Id);
        Assert.DoesNotContain(opened.Motions, motion => motion.Window == dragged.Window);

        var dropped = core.Handle(new PointerButtonReleased("mouse:272") { Position = new Point(1500, 500) });
        Assert.False(Assert.Single(dropped.State.Clients, client => client.Window == dragged.Window).Floating);
    }

    [Fact]
    public void AGridSnappingTileIsPutBackThreeTimesThenCenteredWithoutResizing()
    {
        long now = 0;
        var core = new WindowManager { Clock = () => now };
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var smaller = new Rectangle(10, 20, 1900, 1000);
        for (var i = 0; i < 3; i++)
            Assert.Equal(Area, Assert.Single(core.Handle(new WindowDrifted(new WindowId(1), smaller)).Placements).Bounds);

        var centered = core.Handle(new WindowDrifted(new WindowId(1), smaller));
        var box = Assert.Single(centered.Placements).Bounds;
        Assert.Equal(new Rectangle(10, 40, 1900, 1000), box);
        Assert.Empty(core.Handle(new WindowDrifted(new WindowId(1), box)).Placements);
        now = 1000;
        Assert.Equal(Area, Assert.Single(core.Handle(new WindowDrifted(new WindowId(1), box)).Placements).Bounds);
    }

    [Fact]
    public void AWindowWithoutASizingFrameFloatsUnlessARuleTilesIt()
    {
        var core = new WindowManager();
        var appeared = new WindowAppeared(new WindowId(1), Area)
        {
            Traits = WindowTraits.Normal with { Resizable = false },
            Bounds = new Rectangle(10, 20, 400, 300),
        };
        Assert.True(Assert.Single(core.Handle(appeared).State.Clients).Floating);

        var ruled = new WindowManager(ConfigParser.Parse("windowrule = tile, class:.*"));
        Assert.False(Assert.Single(ruled.Handle(appeared).State.Clients).Floating);
    }

    [Fact]
    public void AnOwnedWindowOpensSilentlyOnItsHiddenOwnersWorkspaceAndIsUrgent()
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(new WindowAppeared(new WindowId(2), Area));

        var opened = core.Handle(new WindowAppeared(new WindowId(3), Area)
        {
            Owner = new WindowId(1),
            Opened = true,
            Traits = WindowTraits.Normal with { Dialog = true },
        });

        Assert.Equal(new WorkspaceId("2"), opened.State.ActiveWorkspace);
        Assert.Equal(new WorkspaceId("1"), Assert.Single(opened.State.Clients, client => client.Window == new WindowId(3)).Workspace);
        Assert.Contains(new WindowId(3), opened.Hide);
        Assert.Contains(opened.Events, item => item is ClientUrgent);
        Assert.Equal(new WindowId(2), core.Handle(new Dispatch("killactive")).Close);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATopmostWindowKeepsItsBoundsAndPinAndOnlyTakesFocusItAlreadyHas(bool foreground)
    {
        var core = new WindowManager();
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        var bounds = new Rectangle(1400, 700, 400, 250);

        var opened = core.Handle(new WindowAppeared(new WindowId(2), Area)
        {
            Traits = WindowTraits.Normal with { Topmost = true, Dialog = true },
            Bounds = bounds,
            Foreground = foreground,
        });

        var client = Assert.Single(opened.State.Clients, client => client.Window == new WindowId(2));
        Assert.True(client.Floating);
        Assert.True(client.Pinned);
        Assert.Equal(bounds, client.Bounds);
        Assert.Equal(new WindowId(foreground ? 2 : 1), core.Handle(new Dispatch("killactive")).Close);
        var switched = core.Handle(new Dispatch("workspace", "2"));
        Assert.DoesNotContain(new WindowId(2), switched.Hide);
        var focused = core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));
        Assert.NotEqual(new WindowId(1), focused.Focus);
    }
}



