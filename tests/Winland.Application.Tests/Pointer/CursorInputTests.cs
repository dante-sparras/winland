namespace Winland.Application.Tests;

public class CursorInputTests
{
    [Fact]
    public void DocumentedCursorDoesNotWarpOrHide()
    {
        var options = CursorOptions.From(ConfigParser.Parse(""));

        Assert.False(options.NoWarps);
        Assert.Equal(0, options.InactiveTimeoutSeconds);
        Assert.False(options.HideOnKeyPress);
    }

    [Fact]
    public void CursorBlockIsHonored()
    {
        var document = ConfigParser.Parse("""
            cursor {
                no_warps = true
                inactive_timeout = 4.5
                hide_on_key_press = on
            }
            """);

        Assert.Empty(document.Warnings);
        var options = CursorOptions.From(document);
        Assert.True(options.NoWarps);
        Assert.Equal(4.5, options.InactiveTimeoutSeconds);
        Assert.True(options.HideOnKeyPress);
    }

    [Fact]
    public void InactiveTimeoutClamps()
    {
        var options = CursorOptions.From(ConfigParser.Parse("cursor:inactive_timeout = 40\n"));

        Assert.Equal(20, options.InactiveTimeoutSeconds);
    }

    [Fact]
    public void ACutCursorKeyWarnsAndIsSkipped()
    {
        var document = ConfigParser.Parse("cursor:persistent_warps = true\n");

        Assert.Empty(document.Settings);
        Assert.Equal(new ConfigWarning(1, "Unsupported keyword 'cursor:persistent_warps'."), Assert.Single(document.Warnings));
    }

    [Fact]
    public void ASystemInputKeyWarnsAndIsNotApplied()
    {
        var document = ConfigParser.Parse("""
            input:follow_mouse = 1
            input:repeat_rate = 25
            input:sensitivity = 0
            input:left_handed = true
            """);

        Assert.Equal(
            [
                new ConfigWarning(2, "Unsupported keyword 'input:repeat_rate'."),
                new ConfigWarning(3, "Unsupported keyword 'input:sensitivity'."),
                new ConfigWarning(4, "Unsupported keyword 'input:left_handed'."),
            ],
            document.Warnings);
        Assert.Equal([new Setting("input:follow_mouse", "1")], document.Settings);
    }

    [Fact]
    public void FollowMouseIsReadAndATabletKeyWarns()
    {
        var document = ConfigParser.Parse("input:follow_mouse = detached\ninput:tablet:enabled = true\n");

        Assert.Equal([new Setting("input:follow_mouse", "detached")], document.Settings);
        Assert.Equal(new ConfigWarning(2, "Unsupported keyword 'input:tablet:enabled'."), Assert.Single(document.Warnings));
        Assert.Equal(2, InputOptions.From(document).FollowMouse);
    }

    [Fact]
    public void WheelDirectionIsNotReversed()
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = , mouse_down, workspace, 2\n")));
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 800, 600)));

        var outcome = core.Handle(new WheelTurned("mouse_up", 1_000));

        Assert.Equal(WorkspaceId.Initial, outcome.State.ActiveWorkspace);
    }

    [Fact]
    public void ConfigurePublishesTheCursorAndSkipsASystemInputKey()
    {
        var document = ConfigParser.Parse("input:repeat_rate = 30\ninput:scroll_factor = 2\n");
        var outcome = new WindowManager().Handle(new Configure(document));

        Assert.Equal(
            [
                new ConfigWarning(1, "Unsupported keyword 'input:repeat_rate'."),
                new ConfigWarning(2, "Unsupported keyword 'input:scroll_factor'."),
            ],
            document.Warnings);
        Assert.Equal(new CursorBehavior(false, 0), outcome.Cursor);
    }

    [Fact]
    public void ClientsRoundTripAndAnOldInputObjectIsIgnored()
    {
        var snapshot = new RecoverySnapshot(
        [
            new RecoveredClient(new WindowId(7), WorkspaceId.Initial, MonitorId.Primary, false, "CabinetWClass", "Home", 9)
            {
                MonitorIdentity = @"\\.\DISPLAY1",
            },
        ]);

        Assert.True(RecoverySnapshot.TryParse(snapshot.ToJson(), out var parsed));
        Assert.Equal(snapshot.Clients, parsed.Clients);
        Assert.DoesNotContain("input", snapshot.ToJson(), StringComparison.Ordinal);

        const string Older = """
            {"clients":[{"window":7,"workspace":"1","monitor":"\\\\.\\DISPLAY1","hidden":false,"class":"CabinetWClass","title":"Home","process":9}],"input":{"keyboardDelay":1},"mirrors":[],"vrr":{},"colors":[]}
            """;
        Assert.True(RecoverySnapshot.TryParse(Older, out var legacy));
        Assert.Equal(snapshot.Clients, legacy.Clients);
    }
}
