using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class DisplayColorTests
{
    private static readonly DisplayColorSupport Both = new(true, true, false, false);

    [Fact]
    public void PresetsMapToTheModesWindowsCanShow()
    {
        Assert.Equal(DisplayColorMode.HighDynamicRange, DisplayColors.ModeOf("HDR"));
        Assert.Equal(DisplayColorMode.HighDynamicRange, DisplayColors.ModeOf("hdredid"));
        Assert.Equal(DisplayColorMode.Wide, DisplayColors.ModeOf("wide"));
        Assert.Equal(DisplayColorMode.Wide, DisplayColors.ModeOf("edid"));
        Assert.Equal(DisplayColorMode.Wide, DisplayColors.ModeOf("dcip3"));
        Assert.Equal(DisplayColorMode.Standard, DisplayColors.ModeOf("srgb"));
        Assert.True(DisplayColors.IsPreset("Adobe"));
        Assert.False(DisplayColors.IsPreset("bt2020"));
    }

    [Fact]
    public void ThePlanTurnsOnlyWhatDiffers()
    {
        Assert.Equal(new DisplayColorPlan(true, null, null), DisplayColors.Plan(DisplayColorMode.HighDynamicRange, Both));
        Assert.Equal(new DisplayColorPlan(null, null, null), DisplayColors.Plan(DisplayColorMode.HighDynamicRange, Both with { HdrOn = true }));
        Assert.Equal(new DisplayColorPlan(false, true, null), DisplayColors.Plan(DisplayColorMode.Wide, Both with { HdrOn = true }));
        Assert.Equal(new DisplayColorPlan(null, null, null), DisplayColors.Plan(DisplayColorMode.Wide, Both with { WideOn = true }));
        Assert.Equal(new DisplayColorPlan(false, false, null), DisplayColors.Plan(DisplayColorMode.Standard, Both with { HdrOn = true, WideOn = true }));
        Assert.Equal(new DisplayColorPlan(null, null, null), DisplayColors.Plan(DisplayColorMode.Standard, Both));
    }

    [Fact]
    public void AModeTheDisplayCannotShowIsNamed()
    {
        Assert.Equal(new DisplayColorPlan(null, null, "HDR"), DisplayColors.Plan(DisplayColorMode.HighDynamicRange, new(false, true, false, false)));
        Assert.Equal(new DisplayColorPlan(false, null, "wide color"), DisplayColors.Plan(DisplayColorMode.Wide, new(true, false, true, false)));
    }

    [Fact]
    public void TheRulesPresetIsAskedForAndReported()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, 0x0, 1, cm, hdr
            monitor = \\.\DISPLAY2, preferred, 100x0, 1, cm, auto
            monitor = \\.\DISPLAY3, preferred, 200x0, 1, cm, bt2020, sdrbrightness, 1.2
            """);
        var core = new Core(document);

        var outcome = core.Handle(new MonitorsChanged([Screen(@"\\.\DISPLAY1", 0), Screen(@"\\.\DISPLAY2", 100), Screen(@"\\.\DISPLAY3", 200)]));

        Assert.Equal([new DisplayColorRequest(@"\\.\DISPLAY1", "hdr")], outcome.DisplayColors);
        Assert.Equal([new ConfigWarning(3, "Invalid color management preset.")], document.Warnings);

        using var monitors = JsonDocument.Parse(core.Execute("j/monitors").Text);
        Assert.Equal(["hdr", "auto", "srgb"], monitors.RootElement.EnumerateArray().Select(monitor => monitor.GetProperty("colorManagementPreset").GetString()));
    }

    [Fact]
    public void TenBitAndTheOverridesDecideTheSwitches()
    {
        var split = new DisplayColorReading(true, true, false, false, true, true, 8);
        var joined = new DisplayColorReading(true, false, false, false, true, false, 8);

        Assert.Equal(new DisplayColorGoal(null, true, null), DisplayColors.Intend(new(@"\\.\DISPLAY1", "auto", TenBit: true), split));
        // Without a wide color switch, 10-bit does not fall back to HDR, which would change the whole picture.
        Assert.Equal(new DisplayColorGoal(null, null, "10-bit color"), DisplayColors.Intend(new(@"\\.\DISPLAY1", "", TenBit: true), joined));
        Assert.Equal(new DisplayColorGoal(null, null, "10-bit color"), DisplayColors.Intend(new(@"\\.\DISPLAY1", "auto", TenBit: true), split with { WideSupported = false }));
        Assert.Equal(new DisplayColorGoal(false, false, "10-bit color"), DisplayColors.Intend(new(@"\\.\DISPLAY1", "srgb", TenBit: true), split));
        Assert.Equal(new DisplayColorGoal(false, false, null), DisplayColors.Intend(new(@"\\.\DISPLAY1", "hdredid"), joined with { HdrSupported = false }));
        Assert.Equal(new DisplayColorGoal(false, null, "wide color"), DisplayColors.Intend(new(@"\\.\DISPLAY1", "wide"), joined));
        Assert.Equal(new DisplayColorGoal(null, null, null), DisplayColors.Intend(new(@"\\.\DISPLAY1", "auto"), split with { HdrOn = true }));

        // On the split switches, HDR still needs wide color. Before they exist, only an explicit veto takes HDR away.
        Assert.Equal(new DisplayColorGoal(null, null, "HDR"), DisplayColors.Intend(new(@"\\.\DISPLAY1", "hdr", HdrOverride: 1), split with { WideSupported = false }));
        Assert.Equal(new DisplayColorGoal(true, null, null), DisplayColors.Intend(new(@"\\.\DISPLAY1", "hdr", WideOverride: 1, HdrOverride: 1), split with { HdrSupported = false, WideSupported = false }));
        Assert.Equal(new DisplayColorGoal(true, null, null), DisplayColors.Intend(new(@"\\.\DISPLAY1", "hdr", HdrOverride: 1), joined with { HdrSupported = false }));
        Assert.Equal(new DisplayColorGoal(null, null, "HDR"), DisplayColors.Intend(new(@"\\.\DISPLAY1", "hdr", WideOverride: -1, HdrOverride: 1), joined));
    }

    [Fact]
    public void TheReplyNamesTheDepthWindowsReports()
    {
        Assert.Equal("XRGB2101010", DisplayColors.Format(10));
        Assert.Equal("XRGB8888", DisplayColors.Format(8));
        Assert.Equal("", DisplayColors.Format(0));
        Assert.Equal(
            @"The display \\.\DISPLAY1 cannot show 10-bit color while cm srgb is set, because Windows only raises bit depth with HDR or wide color.",
            DisplayColors.Explain(@"\\.\DISPLAY1", new(@"\\.\DISPLAY1", "srgb", TenBit: true), new(false, false, "10-bit color")));
        Assert.Equal(
            @"The display \\.\DISPLAY1 cannot show 10-bit color.",
            DisplayColors.Explain(@"\\.\DISPLAY1", new(@"\\.\DISPLAY1", "auto", TenBit: true), new(null, null, "10-bit color")));
    }

    [Fact]
    public void BitDepthBrightnessAndLuminanceAreParsedAndReported()
    {
        var document = ConfigParser.Parse("""
            monitor = \\.\DISPLAY1, preferred, auto, auto, bitdepth, 10, cm, wide, sdrbrightness, 1.2, sdrsaturation, 0.98, sdr_min_luminance, 0.4, sdr_max_luminance, 120, supports_wide_color, 1, supports_hdr, -1, min_luminance, 0.05, max_luminance, 600, max_avg_luminance, 300
            monitor = \\.\DISPLAY1, preferred, 3x4, auto, bitdepth, 8
            monitor = \\.\DISPLAY2, preferred, auto, auto, supports_hdr, 1, sdrbrightness, no
            """);
        var core = new Core(document);
        var screen = Screen(@"\\.\DISPLAY1", 0) with
        {
            BitsPerChannel = 8,
            HdrCapable = true,
            WideCapable = true,
            SeparateColor = true,
            Chroma = true,
            MinLuminance = 0.1f,
            MaxLuminance = 400,
            MaxAvgLuminance = 200,
        };

        var outcome = core.Handle(new MonitorsChanged([screen, Screen(@"\\.\DISPLAY2", 100)]));

        Assert.Equal([new ConfigWarning(3, "Invalid SDR brightness.")], document.Warnings);
        Assert.Equal(
            [new DisplayColorRequest(@"\\.\DISPLAY1", "wide", false, 1, -1)],
            outcome.DisplayColors);
        var shown = Assert.Single(outcome.State.Monitors, monitor => monitor.Name == @"\\.\DISPLAY1");
        Assert.Equal(3, shown.X);
        Assert.Equal(4, shown.Y);
        Assert.Equal("wide", shown.ColorPreset);
        // The depth is the one Windows reports, 8 here, which turning wide color on does not always raise.
        Assert.Equal(8, shown.BitsPerChannel);
        Assert.Equal(1.2, shown.SdrBrightness);
        Assert.Equal(0.98, shown.SdrSaturation);
        Assert.Equal(0.4, shown.SdrMinLuminance);
        Assert.Equal(120, shown.SdrMaxLuminance);
        Assert.Equal(0.05f, shown.MinLuminance);
        Assert.Equal(600, shown.MaxLuminance);
        Assert.Equal(300, shown.MaxAvgLuminance);
        Assert.True(shown.HdrCapable);
        Assert.True(shown.Chroma);

        using var monitors = JsonDocument.Parse(core.Execute("j/monitors").Text);
        var json = Assert.Single(monitors.RootElement.EnumerateArray(), monitor => monitor.GetProperty("name").GetString() == @"\\.\DISPLAY1");
        Assert.Equal("wide", json.GetProperty("colorManagementPreset").GetString());
        Assert.Equal("XRGB8888", json.GetProperty("currentFormat").GetString());
        Assert.Equal(1.2, json.GetProperty("sdrBrightness").GetDouble());
        Assert.True(json.GetProperty("hardwareDetails").GetProperty("hdr").GetBoolean());
        Assert.True(json.GetProperty("hardwareDetails").GetProperty("bt2020").GetBoolean());
        Assert.False(json.GetProperty("hardwareDetails").GetProperty("vrrCapable").GetBoolean());
        Assert.Contains("hdr: yes", core.Execute("monitors").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SavedColorRoundTripsAndAnOlderSnapshotHasNone()
    {
        var snapshot = new RecoverySnapshot([])
        {
            Colors = [new DisplayColorOriginal(@"\\.\DISPLAY1", true, false, true)],
        };

        Assert.True(RecoverySnapshot.TryParse(snapshot.ToJson(), out var parsed));
        Assert.Equal(snapshot.Colors, parsed.Colors);
        Assert.True(RecoverySnapshot.TryParse(new RecoverySnapshot([]).ToJson(), out var none));
        Assert.Null(none.Colors);
        Assert.True(RecoverySnapshot.TryParse("""{"clients":[]}""", out var older));
        Assert.Null(older.Colors);
    }

    private static Monitor Screen(string name, int x) =>
        new(name, name, x, 0, 100, 100, 1, true, new Rectangle(x, 0, 100, 100));
}
