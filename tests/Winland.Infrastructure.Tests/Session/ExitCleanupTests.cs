using System.Runtime.InteropServices;

namespace Winland.Infrastructure.Tests;

[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class ExitCleanupTests
{
    private const int StyleIndex = -16;

    [DesktopFact]
    public void AClientHiddenAfterACleanExitStaysHiddenAndDoesNotRejoin()
    {
        using var probe = new LimitProbe();
        probe.Open();
        _ = ProbeNative.ShowWindow(probe.Anchor, 0);
        var window = new WindowId(probe.Anchor.ToInt64());
        var left = new RecoveredClient(window, WorkspaceId.Initial, MonitorId.Primary, true,
            "WinlandLimitProbe", "WinlandLimitProbe", Environment.ProcessId);
        var core = new WindowManager();
        using var adapter = new WindowAdapter(core);

        adapter.Recover(new RecoverySnapshot([left]) { Clean = true });

        Assert.False(IsWindowVisible(probe.Anchor));
        Assert.False(core.Manages(window));
    }

    [DesktopFact]
    public void AClientMinimizedWhileWinlandWasAwayDoesNotRejoin()
    {
        using var probe = new LimitProbe();
        probe.Open();
        _ = ProbeNative.ShowWindow(probe.Anchor, 6);
        var window = new WindowId(probe.Anchor.ToInt64());
        foreach (var hidden in new[] { false, true })
        {
            if (hidden)
                _ = ProbeNative.ShowWindow(probe.Anchor, 0);

            var left = new RecoveredClient(window, WorkspaceId.Initial, MonitorId.Primary, hidden,
                "WinlandLimitProbe", "WinlandLimitProbe", Environment.ProcessId);
            var core = new WindowManager();
            using var adapter = new WindowAdapter(core);

            adapter.Recover(new RecoverySnapshot([left]));

            Assert.True(ProbeNative.IsIconic(probe.Anchor));
            Assert.True(IsWindowVisible(probe.Anchor));
            Assert.False(core.Manages(window));
        }
    }

    [DesktopFact]
    public void ARestartPutsBackTheFrameACrashedRunLeftStripped()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var probe = new LimitProbe();
        probe.Open();
        try
        {
            var original = (uint)GetWindowLong(probe.Anchor, StyleIndex);
            _ = SetWindowLong(probe.Anchor, StyleIndex, (int)BorderlessStyle.Strip(original));
            var left = new RecoveredClient(
                new WindowId(probe.Anchor.ToInt64()),
                WorkspaceId.Initial,
                MonitorId.Primary,
                false,
                "WinlandLimitProbe",
                "a title from before",
                Environment.ProcessId)
            {
                FrameStyle = original,
            };

            var core = new WindowManager();
            using var adapter = new WindowAdapter(core);
            core.ReducedMotion = () => true;
            adapter.Recover(new RecoverySnapshot([left]));

            Assert.Equal(original, (uint)GetWindowLong(probe.Anchor, StyleIndex));
        }
        finally
        {
            probe.Close();
        }
    }

    [DesktopFact]
    public void ARestartShowsAHiddenClientWhoseTitleChanged()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var probe = new LimitProbe();
        probe.Open();
        try
        {
            _ = ProbeNative.ShowWindow(probe.Anchor, 0);
            Assert.False(IsWindowVisible(probe.Anchor));
            var left = new RecoveredClient(
                new WindowId(probe.Anchor.ToInt64()),
                WorkspaceId.Initial,
                MonitorId.Primary,
                true,
                "WinlandLimitProbe",
                "a title from before",
                Environment.ProcessId);

            var core = new WindowManager();
            using var adapter = new WindowAdapter(core);
            core.ReducedMotion = () => true;
            adapter.Recover(new RecoverySnapshot([left]));

            Assert.True(IsWindowVisible(probe.Anchor));
        }
        finally
        {
            probe.Close();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
}
