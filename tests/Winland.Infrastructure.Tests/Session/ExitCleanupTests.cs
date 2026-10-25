using System.Runtime.InteropServices;

namespace Winland.Infrastructure.Tests;

[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class ExitCleanupTests
{
    private const int StyleIndex = -16;

    [DesktopFact]
    public void ResettingDecorationsPutsBackTheFrameOfAFullscreenClient()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var probe = new LimitProbeTests.LimitProbe();
        probe.Open();
        try
        {
            var core = new WindowManager();
            using var adapter = new WindowAdapter(core);
            core.ReducedMotion = () => true;
            var original = GetWindowLong(probe.Anchor, StyleIndex);
            adapter.AdoptExisting([probe.Anchor]);
            var window = new WindowId(probe.Anchor.ToInt64());
            Assert.True(core.Manages(window));

            adapter.Apply(core.Handle(new Dispatch("fullscreen", "2")));
            Assert.NotEqual(original, GetWindowLong(probe.Anchor, StyleIndex));

            adapter.ResetDecorations();

            Assert.Equal(original, GetWindowLong(probe.Anchor, StyleIndex));
        }
        finally
        {
            probe.Close();
        }
    }

    // The hook worker and the pipe still apply outcomes while the exit cleanup runs. One that arrives after
    // the frames were put back must not strip a frame again: the process ends next, with the state file gone.
    [DesktopFact]
    public void AnOutcomeAfterTheCleanupDoesNotStripTheFrameAgain()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var probe = new LimitProbeTests.LimitProbe();
        probe.Open();
        try
        {
            var core = new WindowManager();
            using var adapter = new WindowAdapter(core);
            core.ReducedMotion = () => true;
            var original = GetWindowLong(probe.Anchor, StyleIndex);
            adapter.AdoptExisting([probe.Anchor]);
            Assert.True(core.Manages(new WindowId(probe.Anchor.ToInt64())));
            adapter.Apply(core.Handle(new Dispatch("fullscreen", "2")));
            adapter.Apply(core.Handle(new Dispatch("fullscreen", "2")));
            Assert.Equal(original, GetWindowLong(probe.Anchor, StyleIndex));

            adapter.RevealHidden();
            adapter.ResetDecorations();
            adapter.Apply(core.Handle(new Dispatch("fullscreen", "2")));

            Assert.Equal(original, GetWindowLong(probe.Anchor, StyleIndex));
        }
        finally
        {
            probe.Close();
        }
    }

    [DesktopFact]
    public void ARestartPutsBackTheFrameACrashedRunLeftStripped()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var probe = new LimitProbeTests.LimitProbe();
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
        using var probe = new LimitProbeTests.LimitProbe();
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
