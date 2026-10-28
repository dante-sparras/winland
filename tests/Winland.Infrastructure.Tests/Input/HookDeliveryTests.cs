namespace Winland.Infrastructure.Tests;

public class HookDeliveryTests
{
    [Fact]
    public void RepeatsAndReleaseKeepTheNameFromThePress()
    {
        using var keys = new KeyboardHook(new WindowManager(), _ => { });

        Assert.Equal("Q", keys.KeyName(0x51, false, false, () => "Q"));
        Assert.Equal("Q", keys.KeyName(0x51, false, false, () => "й"));
        Assert.Equal("Q", keys.KeyName(0x51, false, true, () => "й"));
        Assert.Equal("й", keys.KeyName(0x51, false, false, () => "й"));
    }

    [Fact]
    public void ANonConsumingBindOnAResizeRingRunsOnPressAndRelease()
    {
        var core = BorderCore("bindn = , mouse:272, exec, press\nbindnr = , mouse:272, exec, release\n");
        var outcomes = new List<Outcome>();
        using var keys = new KeyboardHook(core, outcomes.Add);
        using var mouse = new MouseHook(core, keys);
        var position = new Point(960, 540);
        Assert.True(core.OnBorder(position));

        Assert.True(mouse.OnButton("mouse:272", false, position));
        Assert.True(mouse.OnButton("mouse:272", true, position));
        keys.Dispose();
        keys.Pump();

        Assert.Equal(["press", "release"], outcomes.SelectMany(outcome => outcome.Launches).Select(launch => launch.CommandLine));
    }

    [Fact]
    public void RecoveringAMissedBorderReleaseStopsResizing()
    {
        var core = BorderCore("");
        using var keys = new KeyboardHook(core, _ => { });
        using var mouse = new MouseHook(core, keys);

        Assert.True(mouse.OnButton("mouse:272", false, new Point(960, 540)));
        mouse.EndDrags(new Point(1000, 540));
        keys.Dispose();
        keys.Pump();
        var before = core.Handle(new PointerMoved(new Point(1000, 540))).State.Clients.Single(client => client.Window.Value == 1).Bounds;
        var after = core.Handle(new PointerMoved(new Point(1100, 540))).State.Clients.Single(client => client.Window.Value == 1).Bounds;

        Assert.Equal(before, after);
        Assert.False(mouse.OnButton("mouse:272", true, new Point(1100, 540)));
    }

    private static WindowManager BorderCore(string binds)
    {
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("general:resize_on_border = true\ngeneral:gaps_in = 8\ngeneral:gaps_out = 0\ngeneral:border_size = 0\n" + binds)));
        core.Handle(new WindowAppeared(new WindowId(1), new Rectangle(0, 0, 1920, 1080)) { Traits = WindowTraits.Normal });
        core.Handle(new WindowAppeared(new WindowId(2), new Rectangle(0, 0, 1920, 1080)) { Traits = WindowTraits.Normal });
        return core;
    }
}
