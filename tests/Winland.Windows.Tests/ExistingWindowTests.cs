using Winland.Windows;
using Winland.Windows.Tests;

namespace Winland.Tests;

[Collection(DesktopCollection.Name)]
[Trait("Category", DesktopCollection.Name)]
public class ExistingWindowTests
{
    private const int Minimize = 6;

    [DesktopFact]
    public void WindowsOpenBeforeStartupAreFoundAndAdoptedButMinimizedOnesAreSkipped()
    {
        _ = ProbeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var probe = new LimitProbeTests.LimitProbe();
        probe.Open();
        try
        {
            _ = ProbeNative.ShowWindow(probe.Small, Minimize);

            var found = WindowAdapter.ExistingWindows();
            Assert.Contains(probe.Anchor, found);
            Assert.Contains(probe.Wide, found);
            Assert.DoesNotContain(probe.Small, found);

            // Only the probe windows are handed over, so the desktop's own windows are left alone.
            var core = new Core { ReducedMotion = () => true };
            using var adapter = new WindowAdapter(core);
            adapter.AdoptExisting([probe.Anchor, probe.Wide]);

            var windows = core.Recovery().Clients.Select(client => client.Window).ToList();
            Assert.Contains(new WindowId(probe.Anchor.ToInt64()), windows);
            Assert.Contains(new WindowId(probe.Wide.ToInt64()), windows);
            Assert.DoesNotContain(new WindowId(probe.Small.ToInt64()), windows);
        }
        finally
        {
            probe.Close();
        }
    }
}
