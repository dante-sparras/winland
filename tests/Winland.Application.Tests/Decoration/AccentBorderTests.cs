namespace Winland.Application.Tests;

public class AccentBorderTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    private static readonly BorderColor Accent = new(0xFF, 0x11, 0x22, 0x33);

    private static readonly BorderColor Chosen = new(0xFF, 0xAA, 0xBB, 0xCC);

    [Fact]
    public void AnEmptyConfigFollowsTheAccentAndASetColorOverridesIt()
    {
        var core = Configured("");
        var opened = core.Handle(Appeared(1, Area));
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0xFF, 0xFF), Border(opened, 1));
        Assert.Equal(
            "{\"option\":\"general:col.active_border\",\"str\":\"0xFFFFFFFF\",\"set\":false}",
            core.Execute("getoption general:col.active_border").Text);

        var followed = core.Handle(new AccentChanged(Accent));
        Assert.Equal(Accent, Border(followed, 1));
        Assert.Equal(
            "{\"option\":\"general:col.active_border\",\"str\":\"0xFF112233\",\"set\":false}",
            core.Execute("getoption general:col.active_border").Text);

        var changed = core.Handle(new AccentChanged(new BorderColor(0xFF, 0x01, 0x02, 0x03)));
        Assert.Equal(new BorderColor(0xFF, 0x01, 0x02, 0x03), Border(changed, 1));

        var pinned = core.Handle(new Configure(ConfigParser.Parse("general:col.active_border = rgba(aabbccff)\n")));
        Assert.Equal(Chosen, Border(pinned, 1));
        var ignored = core.Handle(new AccentChanged(Accent));
        Assert.Equal(Chosen, Border(ignored, 1));
        Assert.Equal(
            "{\"option\":\"general:col.active_border\",\"str\":\"0xFFAABBCC\",\"set\":true}",
            core.Execute("getoption general:col.active_border").Text);

        var restored = core.Handle(new Configure(ConfigParser.Parse("")));
        Assert.Equal(Accent, Border(restored, 1));
        Assert.Equal(
            "{\"option\":\"general:col.active_border\",\"str\":\"0xFF112233\",\"set\":false}",
            core.Execute("getoption general:col.active_border").Text);
        Assert.Equal(
            "{\"option\":\"general:col.inactive_border\",\"str\":\"0xFF404040\",\"set\":false}",
            core.Execute("getoption general:col.inactive_border").Text);
    }

    [Fact]
    public void AGradientDoesNotOverrideTheAccent()
    {
        var core = Configured("general:col.active_border = rgba(ff00ffff) 90deg\n");
        core.Handle(Appeared(1, Area));

        var followed = core.Handle(new AccentChanged(Accent));

        Assert.Equal(Accent, Border(followed, 1));
        Assert.False(DecorationOptions.From(ConfigParser.Parse("general:col.active_border = rgba(ff00ffff) 90deg\n")).ActiveBorderSet);
    }

    [Fact]
    public void AnInactiveClientKeepsTheDimBorder()
    {
        var core = Configured("");
        core.Handle(Appeared(1, Area));
        core.Handle(Appeared(2, Area));
        var followed = core.Handle(new AccentChanged(Accent));

        var borders = new[] { Border(followed, 1), Border(followed, 2) };
        Assert.Contains(Accent, borders);
        Assert.Contains(new BorderColor(0xFF, 0x40, 0x40, 0x40), borders);
    }

    private static readonly HighContrast Contrast = new(
        new BorderColor(0xFF, 0x1A, 0xEB, 0xFF), new BorderColor(0xFF, 0xFF, 0xFF, 0xFF), new BorderColor(0xFF, 0x3F, 0xF2, 0x3F));

    [Fact]
    public void HighContrastColorsReplaceTheUnsetBorderAndTabColorsUntilItEnds()
    {
        var core = Configured("");
        core.Handle(Appeared(1, Area));
        core.Handle(Appeared(2, Area));
        core.Execute("dispatch togglegroup");

        var contrasted = core.Handle(new AccentChanged(Accent) { Contrast = Contrast });
        var borders = new[] { Border(contrasted, 1), Border(contrasted, 2) };
        Assert.Contains(Contrast.Highlight, borders);
        Assert.Contains(Contrast.GrayText, borders);
        var style = Assert.Single(core.Execute("dispatch resizewindow").Effect.GroupBars).Style;
        Assert.Equal((Contrast.Text, Contrast.Highlight, Contrast.GrayText), (style.TextColor, style.Active, style.Inactive));

        var ended = core.Handle(new AccentChanged(Accent));
        Assert.Contains(new BorderColor(0xFF, 0x40, 0x40, 0x40), new[] { Border(ended, 1), Border(ended, 2) });
    }

    [Fact]
    public void AConfiguredColorWinsOverHighContrast()
    {
        var core = Configured("general:col.inactive_border = rgba(aabbccff)\ngroup:groupbar:text_color = rgba(aabbccff)\n");
        core.Handle(Appeared(1, Area));
        core.Handle(Appeared(2, Area));
        core.Execute("dispatch togglegroup");

        var contrasted = core.Handle(new AccentChanged(Accent) { Contrast = Contrast });

        Assert.Contains(Chosen, new[] { Border(contrasted, 1), Border(contrasted, 2) });
        Assert.Equal(Chosen, Assert.Single(core.Execute("dispatch resizewindow").Effect.GroupBars).Style.TextColor);
    }

    [Fact]
    public void TheTextSizeGrowsTheTabFontAndTitleRow()
    {
        var core = Configured("");
        core.Handle(Appeared(1, Area));
        core.Execute("dispatch togglegroup");
        var plain = Assert.Single(core.Execute("dispatch resizewindow").Effect.GroupBars).Style;

        core.Handle(new AccentChanged(Accent) { TextScale = 1.5 });
        var larger = Assert.Single(core.Execute("dispatch resizewindow").Effect.GroupBars).Style;

        Assert.Equal((12, 21), (larger.FontSize, larger.Height));
        Assert.Equal(plain.IndicatorHeight, larger.IndicatorHeight);
    }

    private static BorderColor Border(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == client.Id).Border;
    }
}
