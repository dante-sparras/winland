using System.Text.Json;
using Winland;

namespace Winland.Tests;

public class IdleInhibitTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    [Fact]
    public void AlwaysKeepsTheSystemAwakeWhileTheClientLives()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = idle_inhibit always, class:^(mpv)$\n"));
        var opened = core.Handle(Window(1, "mpv"));
        core.Handle(Window(2, "kitty"));
        var away = core.Handle(new Dispatch("movetoworkspacesilent", "3,address:0x1"));

        var closed = core.Handle(new WindowClosed(new WindowId(1)));

        Assert.True(IdleInhibit.Wanted(opened.State));
        Assert.True(IdleInhibit.Wanted(away.State));
        Assert.False(IdleInhibit.Wanted(closed.State));
    }

    [Fact]
    public void FocusKeepsTheSystemAwakeOnlyWhileTheClientIsFocused()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = idle_inhibit focus, class:^(mpv)$\n"));
        core.Handle(Window(1, "mpv"));
        var focused = core.Handle(new WindowFocused(new WindowId(1), FocusCause.User));

        core.Handle(Window(2, "kitty"));
        var other = core.Handle(new WindowFocused(new WindowId(2), FocusCause.User));

        Assert.True(IdleInhibit.Wanted(focused.State));
        Assert.False(IdleInhibit.Wanted(other.State));
    }

    [Fact]
    public void FullscreenKeepsTheSystemAwakeOnlyWhileTheClientIsFullscreenAndShown()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = idle_inhibit fullscreen, class:^(mpv)$\n"));
        var tiled = core.Handle(Window(1, "mpv"));

        var fullscreen = core.Handle(new Dispatch("fullscreen"));
        var elsewhere = core.Handle(new Dispatch("workspace", "2"));

        Assert.False(IdleInhibit.Wanted(tiled.State));
        Assert.True(IdleInhibit.Wanted(fullscreen.State));
        Assert.False(IdleInhibit.Wanted(elsewhere.State));
    }

    [Fact]
    public void NoneAndNoRuleDoNotKeepTheSystemAwake()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = idle_inhibit none, class:^(mpv)$\n"));

        var opened = core.Handle(Window(1, "mpv"));
        var other = core.Handle(Window(2, "kitty"));

        Assert.False(IdleInhibit.Wanted(opened.State));
        Assert.False(IdleInhibit.Wanted(other.State));
    }

    [Fact]
    public void AnUnknownModeWarnsAndIsSkipped()
    {
        var document = ConfigParser.Parse("windowrulev2 = idle_inhibit sometimes, class:^(mpv)$\n");

        var warning = Assert.Single(document.Warnings);
        Assert.Contains("idle_inhibit rule has unknown mode \"sometimes\"", warning.Message, StringComparison.Ordinal);
        Assert.Empty(document.WindowRules);
    }

    [Fact]
    public void TheClientsQueryReportsWhichClientInhibitsIdle()
    {
        var core = new Core(ConfigParser.Parse("windowrulev2 = idle_inhibit always, class:^(mpv)$\n"));
        core.Handle(Window(1, "mpv"));
        core.Handle(Window(2, "kitty"));

        var reply = core.Execute("j/clients");

        using var json = JsonDocument.Parse(reply.Text);
        var byAddress = json.RootElement.EnumerateArray().ToDictionary(
            client => client.GetProperty("address").GetString()!,
            client => client.GetProperty("inhibitingIdle").GetBoolean());
        Assert.True(byAddress["0x1"]);
        Assert.False(byAddress["0x2"]);
    }

    [Fact]
    public void AnExecRuleBlockCanInhibitIdle()
    {
        var core = new Core();
        var launch = Assert.IsType<Launch>(core.Handle(new Dispatch("exec", "[idle_inhibit always] mpv")).Launch);
        core.NoteLaunched(77, launch);

        var opened = core.Handle(Window(1, "mpv") with { ProcessId = 77 });

        Assert.True(IdleInhibit.Wanted(opened.State));
    }

    private static WindowAppeared Window(long id, string windowClass) =>
        new(new WindowId(id), Area) { Traits = WindowTraits.Normal with { Class = windowClass } };
}
