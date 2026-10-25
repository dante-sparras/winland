namespace Winland.Application.Tests;

/// <summary>On a workspace with a maximized or fullscreen Client, that Client owns the pointer.</summary>
public class FullscreenFocusTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    public void ThePointerOverACoveredTileDoesNotFocusIt(string mode)
    {
        var core = Desk();
        Move(core, new Point(1500, 500), 1_000);
        core.Handle(new Dispatch("fullscreen", mode));
        Assert.Equal("\"address\":\"0x2\"", Address(core));

        var over = Move(core, new Point(300, 500), 1_100);

        Assert.Null(over.Focus);
        Assert.DoesNotContain(over.Events, item => item is FocusChanged);
        Assert.Equal("\"address\":\"0x2\"", Address(core));
    }

    [Fact]
    public void AMaximizedClientTakesTheFocusWhenThePointerIsOverIt()
    {
        var core = Desk();
        core.Handle(new Dispatch("focuswindow", "address:0x1"));
        core.Handle(new WindowMaximizeRequested(new WindowId(2)));
        Assert.Equal("\"address\":\"0x1\"", Address(core));

        Move(core, new Point(300, 500), 1_000);
        var over = Move(core, new Point(400, 520), 1_100);

        Assert.Equal(new WindowId(2), over.Focus);
    }

    [Fact]
    public void WithNothingCoveringTheTilesStillFollowThePointer()
    {
        var core = Desk();
        Move(core, new Point(1500, 500), 1_000);

        var over = Move(core, new Point(300, 500), 1_100);

        Assert.Equal(new WindowId(1), over.Focus);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("binds:allow_pin_fullscreen = true", true)]
    [InlineData("binds:allow_pin_fullscreen = 1", true)]
    [InlineData("binds:allow_pin_fullscreen = no", false)]
    public void AllowPinFullscreenReadsAFlag(string line, bool expected)
    {
        var session = SessionOptions.From(ConfigParser.Parse(line));

        Assert.Equal(expected, session.AllowPinFullscreen);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("binds:movefocus_cycles_fullscreen = true", true)]
    [InlineData("binds:movefocus_cycles_fullscreen = 0", false)]
    public void MoveFocusCyclesFullscreenReadsAFlag(string line, bool expected)
    {
        var session = SessionOptions.From(ConfigParser.Parse(line));

        Assert.Equal(expected, session.MoveFocusCyclesFullscreen);
    }

    private static WindowManager Desk()
    {
        var core = new WindowManager(ConfigParser.Parse("general:gaps_in = 0\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n"));
        core.Handle(new WindowAppeared(new WindowId(1), Area));
        core.Handle(new WindowAppeared(new WindowId(2), Area));
        return core;
    }

    private static Outcome Move(WindowManager core, Point point, long time) =>
        core.Handle(new PointerMoved(point) { TimeMs = time });

    private static string Address(WindowManager core)
    {
        var text = core.Execute("j/activewindow").Text;
        var at = text.IndexOf("\"address\":", StringComparison.Ordinal);
        Assert.True(at >= 0, text);
        return text[at..(at + "\"address\":\"0x0\"".Length)];
    }
}
