namespace Winland.Infrastructure.Tests;

/// <summary>What the Windows adapter does to the desktop for an Outcome, and in what order.</summary>
public class WindowAdapterTests
{
    private const string Fast = "animation = global, 1, 1, linear\n";

    private static bool Posted(string call, long window) =>
        call.StartsWith($"bounds {window} ", StringComparison.Ordinal) && call.EndsWith(" Post", StringComparison.Ordinal);

    [Fact]
    public void AnOutcomeIsCarriedOutInTheOrderOfItsParts()
    {
        using var windows = new TestWindows(reducedMotion: true);
        var first = windows.Open(1);
        var second = windows.Open(2);
        windows.Dispatch("workspace", "2");
        windows.Open(3);
        windows.Desktop.Add(9, TestWindows.Area);
        var back = windows.Core.Handle(new Dispatch("workspace", "1"));
        Assert.Contains(new WindowId(3), back.Hide);
        Assert.Contains(first, back.Show);
        Assert.Contains(second, back.Show);
        windows.Desktop[1].Visible = new Rectangle(100, 100, 300, 300);
        windows.Desktop[2].Visible = new Rectangle(100, 100, 300, 300);
        windows.Desktop.Calls.Clear();

        windows.Apply(back with
        {
            Closes = [new WindowId(9)],
            Focus = first,
            ZOrder = [new ZOrderRequest(second, true)],
        });

        var desktop = windows.Desktop;
        var order = new[]
        {
            desktop.IndexOf("bounds 1 "),
            desktop.IndexOf("close 9"),
            desktop.IndexOf("show 1 Show"),
            desktop.IndexOf("show 3 Hide"),
            desktop.IndexOf("foreground 1"),
            desktop.IndexOf("raise 2 top"),
        };
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);
    }

    [Fact]
    public void AGroupsNextMemberIsShownUnderTheLastBeforeItIsHidden()
    {
        using var windows = new TestWindows(reducedMotion: true);
        var first = windows.Open(1);
        windows.Dispatch("togglegroup", "");
        var second = windows.Open(2);
        windows.Desktop.Calls.Clear();

        windows.Dispatch("changegroupactive", "b");

        var desktop = windows.Desktop;
        var order = new[]
        {
            desktop.IndexOf($"under {first.Value} {second.Value}"),
            desktop.IndexOf($"show {first.Value} Show"),
            desktop.IndexOf($"show {second.Value} Hide"),
        };
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);
    }

    [Fact]
    public void TransitionsAreTurnedOffOncePerWindowBeforeItsFirstHide()
    {
        using var windows = new TestWindows(Fast);
        windows.Open(1);
        windows.Open(2);
        windows.Dispatch("workspace", "2");
        windows.Settle();
        windows.Dispatch("workspace", "1");
        windows.Settle();

        var calls = windows.Desktop.Calls;
        Assert.Equal(1, calls.Count(call => call == "attr 1 3=1"));
        Assert.Equal(1, calls.Count(call => call == "attr 2 3=1"));
        Assert.True(calls.IndexOf("attr 1 3=1") < windows.Desktop.IndexOf("show 1 Hide"));
        Assert.True(calls.IndexOf("attr 2 3=1") < windows.Desktop.IndexOf("show 2 Hide"));
    }

    [Fact]
    public void TransitionsAreTurnedOnAgainWhenADecorationIsReset()
    {
        using var windows = new TestWindows(reducedMotion: true);
        windows.Open(1);
        var second = windows.Open(2);
        Assert.Equal(1u, windows.Desktop[2].Attributes[3]);

        windows.Apply(windows.Core.Handle(new WindowClosed(second)));

        Assert.Equal(0u, windows.Desktop[2].Attributes[3]);
        Assert.Equal(1u, windows.Desktop[1].Attributes[3]);
    }

    [Fact]
    public void SalvageShowsEveryWindowItHidAndEveryOneTheSnapshotHolds()
    {
        using var windows = new TestWindows(reducedMotion: true);
        windows.Open(1);
        windows.Dispatch("workspace", "2");
        var second = windows.Open(2);
        windows.Dispatch("workspace", "1");
        var snapshot = windows.Adapter.Capture();
        Assert.Equal([second], snapshot!.Clients.Where(client => client.Hidden).Select(client => client.Window));

        // The snapshot is now stale: the second window is shown and the first is hidden, and only the adapter knows.
        windows.Dispatch("workspace", "2");
        Assert.False(windows.Desktop[1].Shown);
        windows.Desktop.Calls.Clear();

        windows.Adapter.Salvage();

        Assert.True(windows.Desktop[1].Shown);
        Assert.True(windows.Desktop[2].Shown);
        Assert.Contains("show 1 Show", windows.Desktop.Calls);
        Assert.Contains("show 2 Show", windows.Desktop.Calls);
    }

    [Fact]
    public void SalvageSkipsAWindowThatIsGone()
    {
        using var windows = new TestWindows(reducedMotion: true);
        windows.Open(1);
        windows.Dispatch("workspace", "2");
        windows.Open(2);
        windows.Dispatch("workspace", "1");
        windows.Desktop.Remove(2);
        windows.Desktop.Calls.Clear();

        windows.Adapter.Salvage();

        Assert.DoesNotContain(windows.Desktop.Calls, call => call.StartsWith("show 2 ", StringComparison.Ordinal));
    }

    [Fact]
    public void AfterSalvageNothingIsCaptured()
    {
        using var windows = new TestWindows(reducedMotion: true);
        windows.Open(1);
        Assert.NotNull(windows.Adapter.Capture());

        windows.Adapter.Salvage();

        Assert.Null(windows.Adapter.Capture());
    }

    [Fact]
    public void AnOutcomeThatAsksForNothingTouchesNothing()
    {
        var published = 0;
        using var windows = new TestWindows(reducedMotion: true, changed: () => published++);
        windows.Open(1);
        Assert.Equal(1, published);
        windows.Desktop.Calls.Clear();

        windows.Adapter.Apply(new Outcome([], new State([])) { IsEmpty = true });

        Assert.Empty(windows.Desktop.Calls);
        Assert.Equal(1, published);
    }

    [Fact]
    public void AHungWindowIsHiddenWithoutWaitingForIt()
    {
        using var windows = new TestWindows(reducedMotion: true);
        windows.Open(1);
        windows.Open(2);
        windows.Desktop[1].Hung = true;
        windows.Desktop.Calls.Clear();

        windows.Dispatch("workspace", "2");

        Assert.Contains("show-async 1 Hide", windows.Desktop.Calls);
        Assert.Contains("show 2 Hide", windows.Desktop.Calls);
        Assert.DoesNotContain("show 1 Hide", windows.Desktop.Calls);
    }

    [Fact]
    public void AHungWindowIsPlacedWithoutWaitingForIt()
    {
        using var windows = new TestWindows(reducedMotion: true);
        windows.Open(1);
        windows.Desktop[1].Hung = true;
        windows.Desktop.Calls.Clear();

        windows.Open(2);

        Assert.Contains(windows.Desktop.Calls, call => Posted(call, 1));
        Assert.DoesNotContain(windows.Desktop.Calls, call => call.StartsWith("bounds 1 ", StringComparison.Ordinal) && call.EndsWith(" Wait", StringComparison.Ordinal));
        Assert.Contains(windows.Desktop.Calls, call => call.StartsWith("bounds 2 ", StringComparison.Ordinal) && call.EndsWith(" Wait", StringComparison.Ordinal));
    }

    [Fact]
    public void AHungWindowIsRestackedWithoutWaitingForIt()
    {
        using var windows = new TestWindows();
        var first = windows.Open(1);
        var second = windows.Open(2);
        windows.Desktop[1].Hung = true;
        windows.Desktop.Calls.Clear();

        windows.Apply(new Outcome([], new State([])) { ZOrder = [new ZOrderRequest(first, true), new ZOrderRequest(second, false)] });

        Assert.Equal(["raise-async 1 top", "raise 2 bottom"], windows.Desktop.Calls);
    }

    [Fact]
    public void AFloatingWindowIsHeldOnTopWhileATileTakesTheFocus()
    {
        using var windows = new TestWindows();
        windows.Open(1);
        windows.Open(2);
        windows.Dispatch("togglefloating");
        windows.Desktop.Calls.Clear();

        windows.Dispatch("focuswindow", "address:0x1");

        var calls = windows.Desktop.Calls;
        Assert.True(calls.IndexOf("hold 2") >= 0, string.Join(" | ", calls));
        Assert.True(calls.IndexOf("hold 2") < calls.IndexOf("foreground 1"), string.Join(" | ", calls));
        Assert.True(calls.IndexOf("foreground 1") < calls.IndexOf("let-go 2"), string.Join(" | ", calls));
        Assert.Equal(0u, windows.Desktop[2].ExStyle & 0x8);
    }

    [Fact]
    public void AWindowItsProgramKeepsOnTopStaysTopmost()
    {
        using var windows = new TestWindows();
        windows.Open(1);
        windows.Open(2);
        windows.Dispatch("togglefloating");
        windows.Desktop[2].ExStyle |= 0x8;
        windows.Desktop.Calls.Clear();

        windows.Dispatch("focuswindow", "address:0x1");

        Assert.DoesNotContain("let-go 2", windows.Desktop.Calls);
        Assert.Equal(0x8u, windows.Desktop[2].ExStyle & 0x8);
    }

    [Fact]
    public void AWindowClosedWithoutAMotionIsTransparentAsItGoesAndComesBackIfItStays()
    {
        using var windows = new TestWindows(reducedMotion: true);
        windows.Open(1);
        windows.Open(2);
        windows.Desktop.Calls.Clear();

        windows.Dispatch("killactive");

        var desktop = windows.Desktop;
        Assert.True(desktop.IndexOf("alpha 2 0") >= 0, "The window was not made transparent.");
        Assert.True(desktop.IndexOf("alpha 2 0") < desktop.IndexOf("close 2"));

        // The program refuses, so the window is put back once the wait for it has passed.
        Assert.Equal(0, desktop[2].Alpha);
        windows.Settle();
        Assert.Equal(0u, desktop[2].ExStyle & 0x00080000);
    }
}
