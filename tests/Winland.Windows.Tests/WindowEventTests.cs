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

    // Each outcome the core decides is numbered, so a gap of one shows that the event asked it nothing.
    [Fact]
    public void AClientsTitleChangeDoesNotGoToTheCore()
    {
        var core = new Core { ReducedMotion = () => true };
        using var adapter = new WindowAdapter(core);
        var window = new WindowId(0x7009);
        var before = core.Handle(new WindowAppeared(window, Area) { Traits = WindowTraits.Normal }).Order!.Value.Sequence;

        adapter.Notice(Events.EVENT_OBJECT_NAMECHANGE, (nint)window.Value);

        var after = core.Dispatch("workspace", "1", allowsElevation: false).Order!.Value.Sequence;
        Assert.Equal(before + 1, after);
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

    // An application that hides its own window unmaps it. Left a Client, it would keep a tile, and
    // its minimum size, that nothing on screen fills.
    [Fact]
    public void AClientItsApplicationHidesStopsBeingAClient()
    {
        var core = new Core { ReducedMotion = () => true };
        using var adapter = new WindowAdapter(core);
        var window = new WindowId(0x700B);
        core.Handle(new WindowAppeared(window, Area) { Traits = WindowTraits.Normal });

        adapter.Notice(Events.EVENT_OBJECT_HIDE, (nint)window.Value);

        Assert.False(core.Manages(window));
    }

    // Claude's screen overlay is layered, click-through, topmost and never activated (0x08280028).
    [Theory]
    [InlineData(0x08280028, true)]
    [InlineData(0x00080020, true)]
    [InlineData(0x00080000, false)]
    [InlineData(0x00000020, false)]
    [InlineData(0x00000100, false)]
    public void ALayeredWindowInputPassesThroughIsAnOverlay(int extended, bool overlay)
    {
        Assert.Equal(overlay, WindowAdapter.IsOverlay(extended));
    }

    // Winland hides the Clients of a Workspace it does not show, and the system reports that hide too.
    [Fact]
    public void AClientWinlandHidStaysAClient()
    {
        var core = new Core();
        using var adapter = new WindowAdapter(core);
        // The adapter reads the system's setting. Without motion the hide happens as the outcome is applied.
        core.ReducedMotion = () => true;
        var window = new WindowId(0x700D);
        core.Handle(new WindowAppeared(window, Area) { Traits = WindowTraits.Normal });
        var away = core.Dispatch("workspace", "2", allowsElevation: false);
        Assert.Contains(window, away.Hide);
        WindowAdapter.Apply(away);

        adapter.Notice(Events.EVENT_OBJECT_HIDE, (nint)window.Value);

        Assert.True(core.Manages(window));
    }
}
