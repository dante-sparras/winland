using Winland.Windows;

namespace Winland.Tests;

/// <summary>Hyprland has no minimize, and a client's maximize request is maximize mode 1.</summary>
public class MinimizeMaximizeTests
{
    private const int Minimize = 6;
    private const int Maximize = 3;

    [Fact]
    public void AMinimizedWindowIsRestoredAndAMaximizedOneBecomesMaximizeMode()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var probe = new LimitProbeTests.LimitProbe();
        probe.Open();
        try
        {
            var core = new Core { ReducedMotion = () => true };
            using var adapter = new WindowAdapter(core);
            adapter.AdoptExisting([probe.Anchor, probe.Wide]);

            _ = ProbeNative.ShowWindow(probe.Anchor, Minimize);
            Assert.True(ProbeNative.IsIconic(probe.Anchor));
            adapter.UndoMinimize(probe.Anchor);
            Assert.False(ProbeNative.IsIconic(probe.Anchor));

            _ = ProbeNative.ShowWindow(probe.Wide, Maximize);
            Assert.True(ProbeNative.IsZoomed(probe.Wide));
            var outcome = adapter.RequestMaximize(probe.Wide);
            Assert.False(ProbeNative.IsZoomed(probe.Wide));
            Assert.Equal(FullscreenMode.Maximized, Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(probe.Wide.ToInt64())).Fullscreen);
        }
        finally
        {
            probe.Close();
        }
    }
}
