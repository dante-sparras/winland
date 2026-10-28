using System.Text.Json;

namespace Winland.Application.Tests.Conformance;

/// <summary>Winland's color management case. The test is named after its case, and sends the same commands.</summary>
[Trait("Category", "Conformance")]
public class ColorsConformance
{
    private const string MonitorNotHonored = "error: Monitor settings other than a reserved area are not honored.";

    [Fact]
    public void MonitorsColorManagement()
    {
        var desk = new Desk();
        ExpectFields(desk, "colorManagementPreset");

        Assert.Equal(MonitorNotHonored, desk.Send("keyword monitor HEADLESS-2, preferred, auto, auto, bitdepth, 10, cm, wide"));
        ExpectFields(desk, "colorManagementPreset");

        Assert.Equal(MonitorNotHonored, desk.Send("keyword monitor HEADLESS-2, preferred, auto, auto, bitdepth, 10, cm, srgb, sdrbrightness, 1.2, sdrsaturation, 0.98"));
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
