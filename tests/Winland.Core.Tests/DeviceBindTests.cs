using Winland;

namespace Winland.Tests;

public class DeviceBindTests
{
    [Fact]
    public void AnInclusiveBindMatchesOnlyTheNamedDevice()
    {
        var core = Load("bindk = , Q, +board, killactive");

        var hit = Press(core, "Q", "board");
        Assert.True(hit.Consumed);
        Assert.Equal("killactive", hit.Dispatcher);

        Assert.False(Press(core, "Q", "other").Consumed);
        Assert.False(Press(core, "Q").Consumed);
        Assert.False(Press(core, "Q", "Board").Consumed);
    }

    [Fact]
    public void AnExclusiveBindMatchesEveryDeviceButTheNamedOne()
    {
        var core = Load("bindk = , Q, -board, killactive");

        Assert.False(Press(core, "Q", "board").Consumed);
        Assert.Equal("killactive", Press(core, "Q", "other").Dispatcher);
        Assert.Equal("killactive", Press(core, "Q").Dispatcher);
    }

    [Fact]
    public void AnEmptyDeviceListMatchesNothingWhenInclusiveAndEverythingWhenExclusive()
    {
        var inclusive = Load("bindk = , Q, +, killactive");
        Assert.False(Press(inclusive, "Q", "board").Consumed);
        Assert.False(Press(inclusive, "Q").Consumed);

        var exclusive = Load("bindk = , Q, -, killactive");
        Assert.Equal("killactive", Press(exclusive, "Q").Dispatcher);
        Assert.Equal("killactive", Press(exclusive, "Q", "board").Dispatcher);
    }

    [Fact]
    public void ATagMatchesAsTheDeviceNameDoes()
    {
        var core = Load("device[board]:tags = laptop, desk\nbindk = , Q, +laptop, killactive\n");

        Assert.Equal("killactive", Press(core, "Q", "board").Dispatcher);
        Assert.False(Press(core, "Q", "board", []).Consumed);
        Assert.Equal("killactive", Press(core, "Q", "elsewhere", ["laptop"]).Dispatcher);
        Assert.False(Press(core, "Q", "Board").Consumed);
    }

    [Fact]
    public void AReleaseUsesTheDeviceTheKeyWentDownOn()
    {
        var core = Load("bindrk = , Q, +board, killactive");
        var session = new BindSession();

        var press = Decide(core, session, new KeyPressed("Q") { Device = "board" });
        Assert.True(press.Consumed);
        Assert.Null(press.Dispatcher);

        var release = Decide(core, session, new KeyReleased("Q"));
        Assert.Equal("killactive", release.Dispatcher);

        var other = new BindSession();
        Decide(core, other, new KeyPressed("Q") { Device = "other" });
        Assert.Null(Decide(core, other, new KeyReleased("Q")).Dispatcher);
    }

    [Fact]
    public void AnAutoConsumingPressIsSwallowedUntilItsDispatcherFails()
    {
        var core = Load("binda = , Q, killactive");

        var decision = Press(core, "Q");

        Assert.True(decision.Consumed);
        Assert.True(decision.PassesOnFailure);
        Assert.Equal(["Q"], decision.ReleaseOnFailure);
        Assert.Equal("killactive", decision.Dispatcher);
        Assert.Contains("\"auto_consuming\":true", core.Execute("j/binds").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAutoConsumingChordDoesNotSwallowItsFirstKey()
    {
        var core = Load("bindsa = , A&B, killactive");
        var session = new BindSession();

        var first = Decide(core, session, new KeyPressed("A"));
        Assert.False(first.Consumed);
        Assert.False(first.PassesOnFailure);

        var second = Decide(core, session, new KeyPressed("B"));
        Assert.True(second.Consumed);
        Assert.True(second.PassesOnFailure);
        Assert.Equal("killactive", second.Dispatcher);
    }

    [Fact]
    public void NonConsumingWinsAndAHardBindSuppressesTheFailurePass()
    {
        var passing = Press(Load("bindna = , Q, killactive"), "Q");
        Assert.False(passing.Consumed);
        Assert.False(passing.PassesOnFailure);
        Assert.Equal("killactive", passing.Dispatcher);

        var hard = Press(Load("bind = , Q, exit\nbinda = , Q, killactive\n"), "Q");
        Assert.True(hard.Consumed);
        Assert.False(hard.PassesOnFailure);
    }

    [Fact]
    public void AnAutoConsumingReleaseBindDoesNotSwallowTheKey()
    {
        var core = Load("bindra = , Q, killactive");
        var session = new BindSession();

        var press = Decide(core, session, new KeyPressed("Q"));
        Assert.False(press.Consumed);
        Assert.False(press.PassesOnFailure);

        var release = Decide(core, session, new KeyReleased("Q"));
        Assert.False(release.Consumed);
        Assert.False(release.PassesOnFailure);
        Assert.Equal("killactive", release.Dispatcher);
    }

    [Fact]
    public void AnInclusiveBindMissesAPointerAndAnExclusiveOneMatchesIt()
    {
        Assert.False(PressButton(Load("bindk = , mouse:272, +board, killactive")).Consumed);

        var exclusive = PressButton(Load("bindk = , mouse:272, -board, killactive"));
        Assert.Equal("killactive", exclusive.Dispatcher);
        Assert.True(exclusive.Consumed);
    }

    [Fact]
    public void TheDeviceFieldAndTagsAreReadAndAnInvalidFieldWarns()
    {
        var comma = LineText.Comma;
        var document = ConfigParser.Parse(
            $"bindakd = SUPER, Q, leave, +a{comma}b\uE001hash##name, exit\n" +
            "bindk = , Q, board, killactive\n" +
            "bindmk = , mouse:272, +pad, killactive\n" +
            "device[board]:tags = one, , two\n" +
            "device[board] {\n    tags = laptop\n}\n" +
            "device:sensitivity = 1\n");

        Assert.Equal(
            [
                new ConfigWarning(2, "Bind device is invalid."),
                new ConfigWarning(8, "Unsupported keyword 'device:sensitivity'."),
            ],
            document.Warnings);
        var binds = document.Binds.OfType<Bind>().ToList();
        Assert.Equal(2, binds.Count);
        Assert.Equal(["a,b", "hash#name"], binds[0].Devices);
        Assert.Equal(BindFlag.AutoConsuming | BindFlag.DeviceInclusive, binds[0].Flags);
        Assert.Equal("leave", binds[0].Description);
        Assert.Equal(["pad"], binds[1].Devices);
        Assert.Equal(BindFlag.Mouse | BindFlag.DeviceInclusive, binds[1].Flags);
        var tag = Assert.Single(document.DeviceTags);
        Assert.Equal("board", tag.Device);
        Assert.Equal(["laptop"], tag.Tags);
    }

    [Fact]
    public void ADeviceBindWaitsOnlyForItsKeyAndModifiers()
    {
        var core = Load("bindk = SUPER, Q, +board, killactive\nbind = , W, killactive\n");
        var table = core.Binds;
        var session = new BindSession();

        Assert.True(table.HasDeviceBind);
        Assert.False(BindMatcher.AwaitsDevice(table, session, "Q", 0, releasing: false));
        Assert.False(BindMatcher.AwaitsDevice(table, session, "W", 0, releasing: false));

        session.Held = Modifier.Super;
        Assert.True(BindMatcher.AwaitsDevice(table, session, "Q", 0, releasing: false));
        Assert.False(BindMatcher.AwaitsDevice(table, session, "q", 0, releasing: true));
        Assert.False(BindMatcher.AwaitsDevice(table, session, "W", 0, releasing: false));
    }

    [Fact]
    public void AReleaseBindWaitsOnThePressAndTheRelease()
    {
        var table = Load("bindrk = , Q, +board, killactive").Binds;
        var session = new BindSession();

        Assert.True(BindMatcher.AwaitsDevice(table, session, "Q", 0, releasing: false));
        Assert.True(BindMatcher.AwaitsDevice(table, session, "Q", 0, releasing: true));
        Assert.False(BindMatcher.AwaitsDevice(table, session, "W", 0, releasing: false));
    }

    [Fact]
    public void NoDeviceBindDoesNotWait()
    {
        var table = Load("bind = , Q, killactive").Binds;

        Assert.False(table.HasDeviceBind);
        Assert.False(BindTable.Empty.HasDeviceBind);
        Assert.False(BindMatcher.AwaitsDevice(table, new BindSession(), "Q", 0, releasing: false));
    }

    [Fact]
    public void AMouseDeviceBindDoesNotWaitForAKey()
    {
        var table = Load("bindk = , mouse:272, +board, killactive").Binds;

        Assert.True(table.HasDeviceBind);
        Assert.False(BindMatcher.AwaitsDevice(table, new BindSession(), "Q", 0, releasing: false));
        Assert.False(BindMatcher.AwaitsDevice(table, new BindSession(), "mouse:272", 0, releasing: false));
    }

    [Fact]
    public void AChordWaitsForEitherOfItsKeys()
    {
        var table = Load("bindsk = , A&B, +board, killactive").Binds;
        var session = new BindSession();

        Assert.True(BindMatcher.AwaitsDevice(table, session, "A", 0, releasing: false));
        Assert.True(BindMatcher.AwaitsDevice(table, session, "b", 0, releasing: false));
        Assert.False(BindMatcher.AwaitsDevice(table, session, "C", 0, releasing: false));
    }

    [Fact]
    public void ASubmapBindWaitsOnlyOnThatSubmap()
    {
        var table = Load("bindk = , Q, +board, killactive\nsubmap = resize\nbindk = , W, +board, killactive\n").Binds;
        var session = new BindSession();

        Assert.True(BindMatcher.AwaitsDevice(table, session, "Q", 0, releasing: false));
        Assert.False(BindMatcher.AwaitsDevice(table, session, "W", 0, releasing: false));

        session.Submap = "resize";
        Assert.False(BindMatcher.AwaitsDevice(table, session, "Q", 0, releasing: false));
        Assert.True(BindMatcher.AwaitsDevice(table, session, "W", 0, releasing: false));
    }

    [Fact]
    public void AnExclusiveBindAndACodeBindWait()
    {
        var exclusive = Load("bindk = , Q, -board, killactive").Binds;
        Assert.True(BindMatcher.AwaitsDevice(exclusive, new BindSession(), "Q", 0, releasing: false));

        var code = Load("bindk = , code:24, +board, killactive").Binds;
        var session = new BindSession();
        Assert.True(BindMatcher.AwaitsDevice(code, session, "other", 24, releasing: false));
        Assert.False(BindMatcher.AwaitsDevice(code, session, "other", 25, releasing: false));
        Assert.False(BindMatcher.AwaitsDevice(code, session, "other", 0, releasing: false));
    }

    [Fact]
    public void AnInhibitedSessionWaitsOnlyForALockedBind()
    {
        var open = Load("bindk = , Q, +board, killactive").Binds;
        var locked = Load("bindlk = , Q, +board, killactive").Binds;
        var session = new BindSession();
        session.Inhibit(true);

        Assert.False(BindMatcher.AwaitsDevice(open, session, "Q", 0, releasing: false));
        Assert.True(BindMatcher.AwaitsDevice(locked, session, "Q", 0, releasing: false));
    }

    private static Core Load(string text)
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(text)));
        return core;
    }

    private static BindDecision Press(Core core, string key, string? device = null, IReadOnlyList<string>? tags = null) =>
        Decide(core, new BindSession(), new KeyPressed(key) { Device = device, DeviceTags = tags });

    private static BindDecision PressButton(Core core) =>
        Decide(core, new BindSession(), new PointerButtonPressed("mouse:272") { Position = new Point(1, 1) });

    private static BindDecision Decide(Core core, BindSession session, Input input) => core.Decide(input, session);
}
