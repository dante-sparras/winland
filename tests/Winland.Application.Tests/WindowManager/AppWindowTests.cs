namespace Winland.Application.Tests;

public class AppWindowTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

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



