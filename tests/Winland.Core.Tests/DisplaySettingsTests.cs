using Winland;

namespace Winland.Tests;

public class DisplaySettingsTests
{
    private static readonly DisplayModeChoice[] Offered =
    [
        new(1920, 1080, 60),
        new(1920, 1080, 144),
        new(2560, 1440, 60),
        new(2560, 1440, 120),
        new(3440, 1440, 100),
        new(1280, 720, 240),
    ];

    [Theory]
    [InlineData("highres", 3440, 1440, 100)]
    [InlineData("highrr", 1280, 720, 240)]
    [InlineData("maxwidth", 3440, 1440, 100)]
    [InlineData("1920x1080", 1920, 1080, 144)]
    [InlineData("1920x1080@60", 1920, 1080, 60)]
    [InlineData("2560x1440@119.88", 2560, 1440, 120)]
    [InlineData("2560x1440@75Hz", 2560, 1440, 60)]
    public void ARuleModePicksFromTheOfferedModes(string mode, int width, int height, int rate) =>
        Assert.Equal(new DisplayModeChoice(width, height, rate), DisplaySettings.Choose(Offered, mode));

    [Fact]
    public void ASizeTheDisplayLacksPicksNothing()
    {
        Assert.Null(DisplaySettings.Choose(Offered, "800x600@60"));
        Assert.Null(DisplaySettings.Choose([], "highres"));
    }

    [Theory]
    [InlineData("highres", true)]
    [InlineData("HighRR", true)]
    [InlineData("1920x1080@60", true)]
    [InlineData("1920x1080", true)]
    [InlineData("1920x", false)]
    [InlineData("x1080", false)]
    [InlineData("1920x1080@", false)]
    [InlineData("modeline 1 2 3", false)]
    public void ModesAreRecognized(string text, bool mode) =>
        Assert.Equal(mode, DisplaySettings.IsMode(text));

    [Theory]
    [InlineData(1, 100)]
    [InlineData(1.25, 125)]
    [InlineData(1.6, 150)]
    [InlineData(1.67, 175)]
    [InlineData(2.6, 250)]
    [InlineData(9, 500)]
    public void ScalesSnapToTheStepsWindowsOffers(double scale, int percent) =>
        Assert.Equal(percent, DisplaySettings.NearestPercent(scale));

    [Fact]
    public void PreferredAsksForNothingAndAModelineWarns()
    {
        var core = new Core(ConfigParser.Parse("monitor = , preferred, auto, 1\n"));
        var listed = core.Handle(new MonitorsChanged([new Monitor("DISPLAY1", "", 0, 0, 1920, 1080, 1, true, new Rectangle(0, 0, 1920, 1040))]));

        Assert.Empty(listed.DisplayModes);
        Assert.Empty(listed.DisplayScales);
        Assert.Contains(ConfigParser.Parse("monitor = , modeline 1 2 3, auto, 1\n").Warnings, warning => warning.Message == "Custom monitor modes are not honored.");
        Assert.Contains(ConfigParser.Parse("monitor = , preferred, auto, zero\n").Warnings, warning => warning.Message == "Invalid scale.");
    }
}
