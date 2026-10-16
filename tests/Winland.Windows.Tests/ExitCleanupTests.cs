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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
}
