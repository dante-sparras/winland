using System.Text.Json;

namespace Winland.Tests.Conformance;

/// <summary>The pinned release's color management case. The test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class ColorsConformance
{
    [Fact]
    public void MonitorsColorManagement()
    {
        var desk = new Desk();
        ExpectFields(desk, "colorManagementPreset");

        desk.Keyword("monitor", "HEADLESS-2, preferred, auto, auto, bitdepth, 10, cm, wide");
        ExpectFields(desk, "colorManagementPreset");

        desk.Keyword("monitor", "HEADLESS-2, preferred, auto, auto, bitdepth, 10, cm, srgb, sdrbrightness, 1.2, sdrsaturation, 0.98");
        ExpectFields(desk, "colorManagementPreset", "sdrBrightness", "sdrSaturation");
    }

    // The reference checks that its monitors reply names these fields.
    private static void ExpectFields(Desk desk, params string[] fields)
    {
        using var monitors = JsonDocument.Parse(desk.Send("j/monitors"));
        foreach (var field in fields)
            Assert.Contains(monitors.RootElement.EnumerateArray(), monitor => monitor.TryGetProperty(field, out _));
    }
}
