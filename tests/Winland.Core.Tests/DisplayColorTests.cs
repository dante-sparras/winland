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
        Assert.Equal(
            [
                new ConfigWarning(3, "Invalid color management preset."),
                new ConfigWarning(3, "Monitor color management other than its cm preset is not honored."),
            ],
            document.Warnings);

        using var monitors = JsonDocument.Parse(core.Execute("j/monitors").Text);
        Assert.Equal(["hdr", "auto", ""], monitors.RootElement.EnumerateArray().Select(monitor => monitor.GetProperty("colorManagementPreset").GetString()));
    }

    private static Monitor Screen(string name, int x) =>
        new(name, name, x, 0, 100, 100, 1, true, new Rectangle(x, 0, 100, 100));
}
