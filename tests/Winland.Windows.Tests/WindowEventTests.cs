using Winland.Windows;
using Events = Windows.Win32.PInvoke;

namespace Winland.Windows.Tests;

/// <summary>Window events driven straight into the adapter. The window ids are not real windows.</summary>
public class WindowEventTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void AWindowDestroyedMidDragLetsTheNextDragStart()
    {
        var core = new Core { ReducedMotion = () => true };
        using var adapter = new WindowAdapter(core);
        var dragged = new WindowId(0x7001);
        var next = new WindowId(0x7003);

        Assert.True(adapter.StartDrag(dragged, sizing: false, new Point(10, 10)));
        Assert.False(adapter.StartDrag(next, sizing: true, new Point(10, 10)));

        adapter.Notice(Events.EVENT_OBJECT_DESTROY, (nint)dragged.Value);

        Assert.True(adapter.StartDrag(next, sizing: true, new Point(10, 10)));
    }

    // Switching virtual desktops cloaks every window on the old one, hidden Clients too. A hidden
    // window is not adopted again when it is uncloaked, so releasing it would leave it hidden for good.
    [Fact]
    public void AHiddenClientThatIsCloakedStaysAClient()
    {
        var core = new Core { ReducedMotion = () => true };
        using var adapter = new WindowAdapter(core);
        var window = new WindowId(0x7005);
        core.Handle(new WindowAppeared(window, Area) { Traits = WindowTraits.Normal });
        var away = core.Dispatch("workspace", "2", allowsElevation: false);
        Assert.Contains(window, away.Hide);

        adapter.Notice(Events.EVENT_OBJECT_CLOAKED, (nint)window.Value);

        Assert.True(core.Manages(window));
    }
}
