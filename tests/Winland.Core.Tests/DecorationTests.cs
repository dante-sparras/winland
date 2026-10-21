using Winland;

namespace Winland.Tests;

public class DecorationTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    private static readonly BorderColor Active = new(0xFF, 0xFF, 0x00, 0x11);

    private static readonly BorderColor Inactive = new(0x80, 0x00, 0x22, 0x33);

    [Theory]
    [InlineData("rgba(33ccffee)", 0x33, 0xCC, 0xFF, 0xEE, false)]
    [InlineData("RGBA(33CCFFEE)", 0x33, 0xCC, 0xFF, 0xEE, false)]
    [InlineData("0xee33ccff", 0x33, 0xCC, 0xFF, 0xEE, false)]
    [InlineData("rgb(112233)", 0x11, 0x22, 0x33, 0xFF, false)]
    public void OneBorderColorIsParsed(string text, byte r, byte g, byte b, byte a, bool gradient)
    {
        Assert.Equal(!gradient, BorderColor.TryParse(text, out var color, out var parsedGradient));
        Assert.Equal(gradient, parsedGradient);
        if (!gradient)
            Assert.Equal(new BorderColor(a, r, g, b), color);
    }

    [Theory]
    [InlineData("rgba(33ccffee) rgba(00ff99ee) 45deg")]
    [InlineData("rgba(ff00ffff) 90deg")]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("rgba(123)")]
    [InlineData("0x112233")]
    public void MalformedBorderColorIsRejected(string text)
    {
        Assert.False(BorderColor.TryParse(text, out _, out _));
    }

    [Fact]
    public void OmittedDecorationUsesTheDocumentedDefaults()
    {
        var options = DecorationOptions.From(ConfigParser.Parse(""));

        Assert.Equal(DecorationOptions.Documented, options);
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0xFF, 0xFF), options.ActiveBorder);
        Assert.Equal(new BorderColor(0xFF, 0x40, 0x40, 0x40), options.InactiveBorder);
        Assert.False(options.ActiveBorderSet);
        Assert.Equal(8, options.Rounding);
        Assert.Equal(2f, options.RoundingPower);
        Assert.True(options.Blur);
        Assert.Equal(1f, options.ActiveOpacity);
        Assert.Equal(1f, options.InactiveOpacity);
        Assert.Equal(1f, options.FullscreenOpacity);
    }

    [Fact]
    public void LatestBorderAndRoundingWinAndAGradientKeepsThePreviousColor()
    {
        var document = ConfigParser.Parse("""
            general:col.active_border = rgba(ff0000ff)
            general:col.active_border = rgba(01020304) rgba(ffffffff) 45deg
            general:col.inactive_border = 0x80aabbcc
            decoration:rounding = 3
            decoration:rounding = no
            """);

        var options = DecorationOptions.From(document);

        Assert.Equal(new BorderColor(0xFF, 0xFF, 0x00, 0x00), options.ActiveBorder);
        Assert.Equal(new BorderColor(0x80, 0xAA, 0xBB, 0xCC), options.InactiveBorder);
        Assert.Equal(3, options.Rounding);
        Assert.Empty(document.Warnings);
    }

    [Fact]
    public void RoundingRejectsANegativeRadiusAndPowerIsClampedToOneThroughTen()
    {
        var options = DecorationOptions.From(ConfigParser.Parse(
            "decoration:rounding = 10\ndecoration:rounding = -1\ndecoration:rounding_power = 0.5\n"));
        var steep = DecorationOptions.From(ConfigParser.Parse("decoration:rounding_power = 12\n"));

        Assert.Equal(10, options.Rounding);
        Assert.Equal(1f, options.RoundingPower);
        Assert.Equal(10f, steep.RoundingPower);
    }

    [Fact]
    public void AdoptedClientGetsTheActiveBorderAndTheOtherGetsTheInactiveOne()
    {
        var core = Configured();

        var first = core.Handle(Appeared(1));
        var only = For(first, 1);
        Assert.Equal(Active, only.Border);
        Assert.Equal(CornerPreference.Round, only.Corners);
        Assert.Empty(first.ResetDecorations);

        var second = core.Handle(Appeared(2));

        Assert.Equal(Inactive, For(second, 1).Border);
        Assert.Equal(Active, For(second, 2).Border);
        Assert.Empty(second.ResetDecorations);
    }

    [Fact]
    public void FocusChangeSwapsTheActiveBorder()
    {
        var core = Configured();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var outcome = core.Handle(new WindowFocused(new WindowId(1)));

        Assert.Equal(Active, For(outcome, 1).Border);
        Assert.Equal(Inactive, For(outcome, 2).Border);
        Assert.Empty(outcome.ResetDecorations);
    }

    [Fact]
    public void ClosingAClientResetsThatWindowAndRecolorsTheSurvivor()
    {
        var core = Configured();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var outcome = core.Handle(new WindowClosed(new WindowId(2)));

        Assert.Equal([new WindowId(2)], outcome.ResetDecorations);
        Assert.Equal(Active, For(outcome, 1).Border);
        Assert.DoesNotContain(outcome.State.Clients, client => client.Window == new WindowId(2));
    }

    [Fact]
    public void WorkspaceSwitchRecolorsTheHiddenClientWithoutResettingIt()
    {
        var core = Configured();
        core.Handle(Appeared(1));

        var away = core.Execute("dispatch workspace 2").Effect;

        Assert.Equal(Inactive, For(away, 1).Border);
        Assert.Empty(away.ResetDecorations);

        var back = core.Execute("dispatch workspace 1").Effect;

        Assert.Equal(Active, For(back, 1).Border);
        Assert.Empty(back.ResetDecorations);
    }

    [Fact]
    public void ConfigReloadRecolorsEveryClient()
    {
        var core = Configured();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        var reloaded = core.Handle(new Configure(ConfigParser.Parse("""
            general:col.active_border = rgba(00ff00ff)
            general:col.inactive_border = rgba(0000ffff)
            decoration:rounding = 0
            """)));

        Assert.Equal(new BorderColor(0xFF, 0x00, 0xFF, 0x00), For(reloaded, 2).Border);
        Assert.Equal(new BorderColor(0xFF, 0x00, 0x00, 0xFF), For(reloaded, 1).Border);
        Assert.Equal(CornerPreference.DoNotRound, For(reloaded, 1).Corners);
        Assert.Empty(reloaded.ResetDecorations);

        var loaded = core.Handle(new ConfigLoaded(ConfigParser.Parse("""
            general:col.active_border = 0xff112233
            decoration:rounding = 4
            """)));

        Assert.Equal(new BorderColor(0xFF, 0x11, 0x22, 0x33), For(loaded, 2).Border);
        Assert.Equal(CornerPreference.RoundSmall, For(loaded, 2).Corners);
    }

    [Fact]
    public void ReloadCommandRecolorsClientsAfterTheConfigIsApplied()
    {
        var core = Configured();
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        var notified = false;
        core.ReadConfig = () => new ConfigRead(ConfigParser.Parse("""
            general:col.active_border = rgba(00ff00ff)
            general:col.inactive_border = rgba(0000ffff)
            decoration:rounding = 0
            """));
        core.AfterReload = _ => notified = true;

        var reply = core.Execute("reload");

        Assert.True(notified);
        Assert.Equal("ok", reply.Text);
        Assert.Equal(new BorderColor(0xFF, 0x00, 0xFF, 0x00), For(reply.Effect, 2).Border);
        Assert.Equal(new BorderColor(0xFF, 0x00, 0x00, 0xFF), For(reply.Effect, 1).Border);
        Assert.Equal(CornerPreference.DoNotRound, For(reply.Effect, 1).Corners);
        Assert.Equal(CornerPreference.DoNotRound, For(reply.Effect, 2).Corners);
        Assert.Empty(reply.Effect.ResetDecorations);
        Assert.Contains(reply.Effect.Events, static item => item is ConfigReloaded);
    }

    [Fact]
    public void WorkspaceSwitchRedecoratesTheShownClient()
    {
        var core = Configured();
        core.Handle(Appeared(1));
        core.Execute("dispatch workspace 2");
        core.Handle(Appeared(2));

        var back = core.Execute("dispatch workspace 1").Effect;

        Assert.Equal(Active, For(back, 1).Border);
        Assert.Equal(Inactive, For(back, 2).Border);
        Assert.Equal(CornerPreference.Round, For(back, 1).Corners);
        Assert.Empty(back.ResetDecorations);
        Assert.Contains(back.Show, window => window == new WindowId(1));
        Assert.Contains(back.Hide, window => window == new WindowId(2));
    }

    [Fact]
    public void WorkspaceSwitchAcrossMonitorsRedecoratesTheShownClient()
    {
        var core = Configured();
        var left = Display(@"\\.\DISPLAY1", 0);
        var right = Display(@"\\.\DISPLAY2", 800);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        var placed = core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));
        Assert.Equal(Active, For(placed, 1).Border);
        Assert.Equal(Inactive, For(placed, 2).Border);

        var moved = core.Execute("dispatch workspace 2").Effect;

        Assert.Equal(Inactive, For(moved, 1).Border);
        Assert.Equal(Active, For(moved, 2).Border);
        Assert.Equal(CornerPreference.Round, For(moved, 2).Corners);
        Assert.Empty(moved.ResetDecorations);
        Assert.Equal(new WindowId(2), moved.Focus);
        Assert.Empty(moved.Show);
        Assert.Empty(moved.Hide);
        Assert.Empty(moved.State.Hidden);
    }

    [Fact]
    public void HotplugRedecoratesClientsOnTheRemainingMonitor()
    {
        var core = Configured();
        var left = Display(@"\\.\DISPLAY1", 0);
        var right = Display(@"\\.\DISPLAY2", 800);
        core.Handle(new MonitorsChanged([left, right]));
        core.Handle(new WindowAppeared(new WindowId(1), left.WorkArea));
        core.Handle(new WindowAppeared(new WindowId(2), right.WorkArea));

        var outcome = core.Handle(new MonitorsChanged([left]));

        Assert.Equal(Active, For(outcome, 1).Border);
        Assert.Equal(Inactive, For(outcome, 2).Border);
        Assert.Empty(outcome.ResetDecorations);
        Assert.Equal(2, outcome.Decorations.Count);
    }

    [Fact]
    public void UnmanagedWindowIsNotDecorated()
    {
        var core = Configured();

        var outcome = core.Handle(new WindowAppeared(new WindowId(9), Area)
        {
            Traits = WindowTraits.Normal with { Taskbar = true },
        });

        Assert.Empty(outcome.State.Clients);
        Assert.Empty(outcome.Decorations);
        Assert.Empty(outcome.ResetDecorations);
    }

    private static Core Configured()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse($"""
            general:col.active_border = rgba({Active.R:x2}{Active.G:x2}{Active.B:x2}{Active.A:x2})
            general:col.inactive_border = 0x{Inactive.A:x2}{Inactive.R:x2}{Inactive.G:x2}{Inactive.B:x2}
            decoration:rounding = 8
            """)));
        return core;
    }

    private static WindowAppeared Appeared(long window) => new(new WindowId(window), Area);

    private static Monitor Display(string name, int x) =>
        new(name, name, x, 0, 800, 600, 1, true, new Rectangle(x, 0, 800, 600));

    private static Decoration For(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == client.Id);
    }
}
