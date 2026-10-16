using System.Runtime.InteropServices;
using Winland.Windows;

namespace Winland.Tests;

public class ExitCleanupTests
{
    private const int StyleIndex = -16;

    [Fact]
    public void ResettingDecorationsPutsBackTheFrameOfAFullscreenClient()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var probe = new LimitProbeTests.LimitProbe();
        probe.Open();
        try
        {
            var core = new Core { ReducedMotion = () => true };
            using var adapter = new WindowAdapter(core);
            var original = GetWindowLong(probe.Anchor, StyleIndex);
            adapter.AdoptExisting([probe.Anchor]);
            var window = new WindowId(probe.Anchor.ToInt64());
            Assert.True(core.Manages(window));

            WindowAdapter.Apply(core.Dispatch("fullscreen", "2", allowsElevation: false));
            Assert.NotEqual(original, GetWindowLong(probe.Anchor, StyleIndex));

            adapter.ResetDecorations();

            Assert.Equal(original, GetWindowLong(probe.Anchor, StyleIndex));
        }
        finally
        {
            probe.Close();
        }
    }

    [Fact]
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

            var core = new Core { ReducedMotion = () => true };
            using var adapter = new WindowAdapter(core);
            adapter.Recover(new RecoverySnapshot([left]));

            Assert.Equal(original, (uint)GetWindowLong(probe.Anchor, StyleIndex));
        }
        finally
        {
            probe.Close();
        }
    }

    [Fact]
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

            var core = new Core { ReducedMotion = () => true };
            using var adapter = new WindowAdapter(core);
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
