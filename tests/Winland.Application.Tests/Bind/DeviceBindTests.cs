namespace Winland.Application.Tests;

public class DeviceBindTests
{
    [Fact]
    public void AnInclusiveBindMatchesOnlyTheNamedDeviceAndDoesNotConsume()
    {
        var core = Load("bindnk = , Q, +board, killactive");

        var hit = Press(core, "Q", "board");
        Assert.False(hit.Consumed);
        Assert.Equal("killactive", hit.Dispatcher);

        Assert.Null(Press(core, "Q", "other").Dispatcher);
        Assert.Null(Press(core, "Q").Dispatcher);
        Assert.Null(Press(core, "Q", "Board").Dispatcher);
    }

    [Fact]
    public void AnExclusiveBindMatchesEveryNamedDeviceButTheNamedOne()
    {
        var core = Load("bindnk = , Q, -board, killactive");

        Assert.Null(Press(core, "Q", "board").Dispatcher);
        Assert.Null(Press(core, "Q").Dispatcher);
        var other = Press(core, "Q", "other");
        Assert.False(other.Consumed);
        Assert.Equal("killactive", other.Dispatcher);
    }

    [Fact]
    public void AnEmptyDeviceListMatchesNothingWhenInclusiveAndOnlyANamedDeviceWhenExclusive()
    {
        var inclusive = Load("bindnk = , Q, +, killactive");
        Assert.Null(Press(inclusive, "Q", "board").Dispatcher);
        Assert.Null(Press(inclusive, "Q").Dispatcher);

        var exclusive = Load("bindnk = , Q, -, killactive");
        Assert.Null(Press(exclusive, "Q").Dispatcher);
        Assert.Equal("killactive", Press(exclusive, "Q", "board").Dispatcher);
    }

    [Fact]
    public void ADeviceTagIsNotABindName()
    {
        var document = ConfigParser.Parse("device[board]:tags = laptop, desk\nbindnk = , Q, +laptop, killactive\n");
        var core = new WindowManager();
        core.Handle(new ConfigLoaded(document));

        Assert.Equal(new ConfigWarning(1, "Unsupported keyword 'device[board]:tags'."), Assert.Single(document.Warnings));
        Assert.Null(Press(core, "Q", "board").Dispatcher);
        Assert.Equal("killactive", Press(core, "Q", "laptop").Dispatcher);
    }

    [Fact]
    public void AReleaseUsesTheDeviceTheKeyWentDownOn()
    {
        var core = Load("bindrnk = , Q, +board, killactive");
        var session = new BindSession();

        var press = Decide(core, session, new KeyPressed("Q") { Device = "board" });
        Assert.False(press.Consumed);
        Assert.Null(press.Dispatcher);

        var release = Decide(core, session, new KeyReleased("Q"));
        Assert.False(release.Consumed);
        Assert.Equal("killactive", release.Dispatcher);

        var other = new BindSession();
        Decide(core, other, new KeyPressed("Q") { Device = "other" });
        Assert.Null(Decide(core, other, new KeyReleased("Q")).Dispatcher);
    }

    [Fact]
    public void APacketAfterThePressFiresTheDeviceBind()
    {
        var core = Load("bindnk = , Q, +board, killactive");
        var session = new BindSession();

        var press = Decide(core, session, new KeyPressed("Q"));
        Assert.False(press.Consumed);
        Assert.Null(press.Dispatcher);

        var packet = Device(core, session, "Q", "board");
        Assert.False(packet.Consumed);
        Assert.Equal("killactive", packet.Dispatcher);
        Assert.Null(Device(core, session, "Q", "other").Dispatcher);
        Assert.Null(Device(core, session, "Q", null).Dispatcher);
    }

    [Fact]
    public void AnEmptyDeviceDoesNotUseTheKeyboardThePressRemembered()
    {
        var core = Load("bindnk = , Q, +board, killactive\nbindrnk = , Q, +board, exit\n");
        var session = new BindSession();

        Assert.Equal("killactive", Decide(core, session, new KeyPressed("Q") { Device = "board" }).Dispatcher);
        Assert.Null(Device(core, session, "Q", null, remember: false).Dispatcher);
        Assert.Equal("exit", Decide(core, session, new KeyReleased("Q")).Dispatcher);
    }

    [Fact]
    public void ARepeatFiresOnlyARepeatingDeviceBind()
    {
        var core = Load("bindenk = , Q, +board, killactive\nbindnk = , W, +board, exit\n");
        var session = new BindSession();
        Decide(core, session, new KeyPressed("Q"));
        Decide(core, session, new KeyPressed("W"));

        Assert.Equal("killactive", Device(core, session, "Q", "board", repeat: true).Dispatcher);
        Assert.Null(Device(core, session, "W", "board", repeat: true).Dispatcher);
    }

    [Fact]
    public void ThePressMaskIsTheOneFromThePress()
    {
        var core = Load("bindnk = SUPER, Q, +board, killactive");
        var session = new BindSession();
        Decide(core, session, new KeyPressed("Super_L"));
        Decide(core, session, new KeyPressed("Q"));
        Decide(core, session, new KeyReleased("Super_L"));

        Assert.Null(Device(core, session, "Q", "board").Dispatcher);
        Assert.Equal("killactive", Device(core, session, "Q", "board", pressedUnder: Modifier.Super).Dispatcher);
    }

    [Fact]
    public void AFastTapChordUsesTheKeysHeldAtThePress()
    {
        var core = Load("bindsnk = , A&B, +board, killactive");
        var session = new BindSession();
        Decide(core, session, new KeyPressed("A"));
        Decide(core, session, new KeyPressed("B"));
        Decide(core, session, new KeyReleased("B"));
        Decide(core, session, new KeyReleased("A"));

        var fired = Device(core, session, "B", "board", remember: false, order: [("A", 0), ("B", 0)]);
        Assert.Equal("killactive", fired.Dispatcher);
        Assert.False(fired.Consumed);
    }

    [Fact]
    public void AFastTapReleaseChordIsArmedFromThePacket()
    {
        var core = Load("bindrsnk = , A&B, +board, killactive");
        var session = new BindSession();
        Decide(core, session, new KeyPressed("A"));
        Decide(core, session, new KeyPressed("B"));
        Decide(core, session, new KeyReleased("B"));
        Decide(core, session, new KeyReleased("A"));
        (string Key, int ScanCode)[] order = [("A", 0), ("B", 0)];

        Assert.Null(Device(core, session, "B", "board", remember: false, order: order).Dispatcher);
        var release = Device(core, session, "B", "board", up: true, remember: false, order: order);
        Assert.Equal("killactive", release.Dispatcher);
        Assert.True(release.DisarmLongPress);
    }

    [Fact]
    public void ADeviceBindRunsInTheSubmapItWasPressedOn()
    {
        var core = Load("bindnk = , Q, +board, killactive\nsubmap = resize\nbindnk = , W, +board, exit\n");
        var session = new BindSession();
        Decide(core, session, new KeyPressed("W"));

        Assert.Null(Device(core, session, "W", "board").Dispatcher);
        Assert.Equal("exit", Device(core, session, "W", "board", submap: "resize").Dispatcher);
    }

    [Fact]
    public void ACodeBindMatchesTheScanCode()
    {
        var core = Load("bindnk = , code:24, +board, killactive");
        var session = new BindSession();

        Assert.Equal("killactive", Device(core, session, "other", "board", scanCode: 24).Dispatcher);
        Assert.Null(Device(core, session, "other", "board", scanCode: 25).Dispatcher);
        Assert.Null(Device(core, session, "other", "board", scanCode: 0).Dispatcher);
    }

    [Fact]
    public void AnInhibitedSessionRunsOnlyALockedDeviceBind()
    {
        var open = Load("bindnk = , Q, +board, killactive");
        var locked = Load("bindlnk = , Q, +board, killactive");
        var session = new BindSession();
        session.Inhibit(true);
        Decide(open, session, new KeyPressed("Q"));

        Assert.Null(Device(open, session, "Q", "board").Dispatcher);
        Assert.Equal("killactive", Device(locked, session, "Q", "board").Dispatcher);
    }

    [Fact]
    public void ALongPressArmsWhenThePacketNamesTheKeyboard()
    {
        var core = Load("bindonk = , Q, +board, killactive");
        var session = new BindSession();
        var press = Decide(core, session, new KeyPressed("Q"));
        Assert.Null(press.ArmLongPressMs);
        Assert.Null(session.LongPressKey);

        var packet = Device(core, session, "Q", "board");
        Assert.Equal(core.Binds.LongPressDelay, packet.ArmLongPressMs);
        Assert.False(packet.DisarmLongPress);
        Assert.Equal("Q", session.LongPressKey);

        Assert.Equal("killactive", Decide(core, session, new LongPressElapsed()).Dispatcher);
    }

    [Fact]
    public void APacketDoesNotRearmALongPressTheHookAlreadyArmed()
    {
        var core = Load("bindo = , Q, exit\nbindonk = , Q, +board, killactive\n");
        var session = new BindSession();
        var press = Decide(core, session, new KeyPressed("Q"));
        Assert.Equal(core.Binds.LongPressDelay, press.ArmLongPressMs);

        var packet = Device(core, session, "Q", "board");
        Assert.Null(packet.ArmLongPressMs);
        Assert.Equal("Q", session.LongPressKey);

        var elapsed = Decide(core, session, new LongPressElapsed());
        Assert.Equal(["exit", "killactive"], elapsed.Dispatches.Select(dispatch => dispatch.Dispatcher));
    }

    [Fact]
    public void AHeldKeyShadowsTheDeviceReleaseBind()
    {
        var core = Load("bindrnk = ALT, Alt_L, +board, killactive\nbind = ALT, P, exit\n");
        var alone = new BindSession();
        Decide(core, alone, new KeyPressed("Alt_L"));
        Device(core, alone, "Alt_L", "board");
        Assert.Equal("killactive", Decide(core, alone, new KeyReleased("Alt_L")).Dispatcher);

        var session = new BindSession();
        Decide(core, session, new KeyPressed("Alt_L"));
        Device(core, session, "Alt_L", "board");
        Assert.Equal("exit", Decide(core, session, new KeyPressed("P")).Dispatcher);
        Decide(core, session, new KeyReleased("P"));
        Assert.Null(Decide(core, session, new KeyReleased("Alt_L")).Dispatcher);
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
    public void AConsumingDeviceBindWarnsOnceAndIsNotRegistered()
    {
        var document = ConfigParser.Parse(
            "bindk = , Q, +board, killactive\n" +
            "bindk = , W, +other, exit\n" +
            "bindnk = , E, +board, killactive\n");

        Assert.Equal([new ConfigWarning(1, ConfigParser.ConsumingDeviceBind)], document.Warnings);
        var bind = Assert.Single(document.Binds.OfType<Bind>());
        Assert.Equal("E", bind.Key);
        Assert.True(bind.Flags.HasFlag(BindFlag.NonConsuming));

        var core = new WindowManager();
        core.Handle(new ConfigLoaded(document));
        Assert.True(core.Binds.HasDeviceBind);
        Assert.Equal("killactive", Press(core, "E", "board").Dispatcher);
        Assert.Null(Press(core, "Q", "board").Dispatcher);
        Assert.Null(Press(core, "W", "other").Dispatcher);
    }

    [Fact]
    public void AMouseDeviceBindIsRegisteredOnlyWhenItDoesNotConsumeAndNeverRuns()
    {
        var document = ConfigParser.Parse("bindk = , mouse:272, +board, killactive\nbindnk = , mouse:272, +board, exit\n");
        Assert.Equal([new ConfigWarning(1, ConfigParser.ConsumingDeviceBind)], document.Warnings);
        var bind = Assert.Single(document.Binds.OfType<Bind>());
        Assert.Equal("mouse:272", bind.Key);
        Assert.Equal(["board"], bind.Devices);

        var core = new WindowManager();
        core.Handle(new ConfigLoaded(document));
        Assert.True(core.Binds.HasDeviceBind);
        var press = Decide(core, new BindSession(), new PointerButtonPressed("mouse:272") { Position = new Point(1, 1) });
        Assert.False(press.Consumed);
        Assert.Null(press.Dispatcher);
    }

    [Fact]
    public void TheDeviceFieldAndTagsAreReadAndAnInvalidFieldWarns()
    {
        var document = ConfigParser.Parse(
            "bindnakd = SUPER, Q, leave, +ab\uE001hash##name, exit\n" +
            "bindk = , Q, board, killactive\n" +
            "bindk = , W, +board, killactive\n" +
            "bindmk = , mouse:272, +pad, killactive\n" +
            "device[board]:tags = one, , two\n" +
            "device[board] {\n    tags = laptop\n}\n" +
            "device:sensitivity = 1\n");

        Assert.Equal(
            [
                new ConfigWarning(2, "Bind device is invalid."),
                new ConfigWarning(3, ConfigParser.ConsumingDeviceBind),
                new ConfigWarning(5, "Unsupported keyword 'device[board]:tags'."),
                new ConfigWarning(7, "Unsupported keyword 'device[board]:tags'."),
                new ConfigWarning(9, "Unsupported keyword 'device:sensitivity'."),
            ],
            document.Warnings);
        var bind = Assert.Single(document.Binds.OfType<Bind>());
        Assert.Equal(["ab", "hash#name"], bind.Devices);
        Assert.Equal(BindFlag.NonConsuming | BindFlag.AutoConsuming | BindFlag.DeviceInclusive, bind.Flags);
        Assert.Equal("leave", bind.Description);
    }

    private static BindDecision Press(WindowManager core, string key, string? device = null) =>
        Decide(core, new BindSession(), new KeyPressed(key) { Device = device });

    private static BindDecision Decide(WindowManager core, BindSession session, Input input) => core.Decide(input, session);

    private static BindDecision Device(
        WindowManager core,
        BindSession session,
        string key,
        string? device,
        bool up = false,
        bool repeat = false,
        bool remember = true,
        int scanCode = 0,
        Modifier? pressedUnder = null,
        string? submap = null,
        (string Key, int ScanCode)[]? order = null) =>
        BindMatcher.DispatchDevice(
            core.Binds,
            session,
            key,
            scanCode,
            up,
            repeat,
            device,
            remember,
            submap: submap,
            pressedUnder: pressedUnder,
            order: order);
}
