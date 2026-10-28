namespace Winland.Infrastructure.Tests;

/// <summary>Winland has no minimize, and a client's maximize request is maximize mode 1.</summary>
[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class MinimizeMaximizeTests
{
    private const int Minimize = 6;
    private const int Maximize = 3;

    [DesktopFact]
    public void AMinimizedWindowIsRestoredAndAMaximizedOneBecomesMaximizeMode()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var probe = new LimitProbe();
        probe.Open();
        try
        {
            var core = new WindowManager();
            using var adapter = new WindowAdapter(core);
            core.ReducedMotion = () => true;
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
