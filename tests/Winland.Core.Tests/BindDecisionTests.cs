using Winland;

namespace Winland.Tests;

public class BindDecisionTests
{
    [Fact]
    public void DecisionDoesNotWaitForTheCoreLock()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, Q, killactive")));
        var session = new BindSession();
        core.Decide(new KeyPressed("Super_L"), session);

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var held = new Thread(() => core.WhileLocked(() =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
        }));
        held.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));

        Exception? failure = null;
        BindDecision decision = default;
        var decided = new Thread(() =>
        {
            try
            {
                decision = core.Decide(new KeyPressed("Q"), session);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        decided.Start();
        try
        {
            Assert.True(decided.Join(TimeSpan.FromMilliseconds(500)), "The bind decision waited on the core lock.");
        }
        finally
        {
            release.Set();
            held.Join();
        }

        Assert.Null(failure);
        Assert.True(decision.Consumed);
        Assert.Equal("killactive", decision.Dispatcher);
    }

    [Fact]
    public void DecisionSwallowsTheReleaseOfABindItRanAndPassesTheModifierRelease()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, Q, killactive")));
        var session = new BindSession();
        core.Decide(new KeyPressed("Super_L"), session);

        var ran = core.Decide(new KeyPressed("Q"), session);
        var keyUp = core.Decide(new KeyReleased("Q"), session);
        var superUp = core.Decide(new KeyReleased("Super_L"), session);

        Assert.Equal("killactive", ran.Dispatcher);
        Assert.True(ran.Consumed);
        Assert.True(ran.Mask);
        Assert.True(keyUp.Consumed);
        Assert.Null(keyUp.Dispatcher);
        Assert.False(superUp.Consumed);
        Assert.Null(superUp.Dispatcher);
        Assert.False(core.Decide(new KeyPressed("E"), session).Consumed);
    }

    // The pinned release folds the released key's own modifier into the mask on release,
    // so the classic "bindr = SUPER, Super_L" runs when Super is let go.
    [Theory]
    [InlineData("bindr = SUPER, Super_L, exec, menu", "Super_L")]
    [InlineData("bindr = , Super_L, exec, menu", "Super_L")]
    [InlineData("bindr = ALT, Alt_R, exec, menu", "Alt_R")]
    [InlineData("bindr = CTRL SHIFT, Shift_L, exec, menu", "Shift_L")]
    public void AReleaseBindOnAModifierKeyRunsWhenThatKeyIsLetGo(string config, string key)
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse(config)));
        var session = new BindSession();
        if (config.Contains("CTRL", StringComparison.Ordinal))
            core.Decide(new KeyPressed("Control_L"), session);

        var down = core.Decide(new KeyPressed(key), session);
        var up = core.Decide(new KeyReleased(key), session);

        Assert.Null(down.Dispatcher);
        Assert.Equal("exec", up.Dispatcher);
    }

    [Fact]
    public void AReleaseBindOnAModifierKeyStillNeedsItsOtherModifiers()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bindr = SUPER ALT, Super_L, exec, menu")));
        var session = new BindSession();

        core.Decide(new KeyPressed("Super_L"), session);
        var up = core.Decide(new KeyReleased("Super_L"), session);

        Assert.Null(up.Dispatcher);
    }

    [Fact]
    public void DecisionHonorsInhibitAndTheLockedFlag()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = , Q, killactive\nbindl = , M, killactive\n")));
        var session = new BindSession();
        session.Inhibit(true);

        Assert.False(core.Decide(new KeyPressed("Q"), session).Consumed);
        Assert.Equal("killactive", core.Decide(new KeyPressed("M"), session).Dispatcher);
    }

    [Fact]
    public void ReloadReplacesTheBindTableAtomically()
    {
        var core = new Core();
        var killQ = ConfigParser.Parse("bind = SUPER, Q, killactive");
        var killW = ConfigParser.Parse("bind = SUPER, W, killactive");
        core.Handle(new Configure(killQ));

        var stop = 0;
        var torn = 0;
        var samples = 0;
        var reader = new Thread(() =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                var entries = core.Binds.Entries;
                var q = false;
                var w = false;
                foreach (var bind in entries)
                {
                    if (bind.Key == "Q")
                        q = true;
                    if (bind.Key == "W")
                        w = true;
                }

                if (q == w)
                    Interlocked.Increment(ref torn);

                Interlocked.Increment(ref samples);
            }
        });
        reader.Start();
        // The reader may start late on a busy machine. Do not stop it before it has sampled.
        SpinWait.SpinUntil(() => Volatile.Read(ref samples) > 0, TimeSpan.FromSeconds(10));
        for (var i = 0; i < 200; i++)
            core.Handle(new Configure(i % 2 == 0 ? killW : killQ));

        Volatile.Write(ref stop, 1);
        reader.Join();
        Assert.True(samples > 0);
        Assert.Equal(0, torn);

        core.Handle(new Configure(killW));
        var session = new BindSession();
        core.Decide(new KeyPressed("Super_L"), session);
        Assert.Null(core.Decide(new KeyPressed("Q"), session).Dispatcher);
        Assert.Equal("killactive", core.Decide(new KeyPressed("W"), session).Dispatcher);
    }

    [Fact]
    public void EveryMatchingBindIsReturnedInConfigOrder()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = , Q, killactive\nbind = , Q, workspace, 2\n")));
        var session = new BindSession();

        var decision = core.Decide(new KeyPressed("Q"), session);

        Assert.True(decision.Consumed);
        Assert.Equal("killactive", decision.Dispatcher);
        Assert.Equal("", decision.Arguments);
        Assert.Equal(
            new BindDispatch[]
            {
                new("killactive", ""),
                new("workspace", "2"),
            },
            decision.Dispatches);
    }

    [Fact]
    public void KeyIsConsumedWhenAnyMatchingBindConsumes()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bindn = , Q, exec, first\nbind = , Q, killactive\n")));
        var session = new BindSession();

        var decision = core.Decide(new KeyPressed("Q"), session);
        var release = core.Decide(new KeyReleased("Q"), session);

        Assert.True(decision.Consumed);
        Assert.Equal(
            new BindDispatch[]
            {
                new("exec", "first"),
                new("killactive", ""),
            },
            decision.Dispatches);
        Assert.True(release.Consumed);
        Assert.Null(release.Dispatcher);
    }

    [Fact]
    public void NonConsumingBindRunsWithoutSwallowingTheKey()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bindn = , Q, exec, notepad\n")));
        var session = new BindSession();

        var decision = core.Decide(new KeyPressed("Q"), session);
        var release = core.Decide(new KeyReleased("Q"), session);

        Assert.False(decision.Consumed);
        Assert.Equal("exec", decision.Dispatcher);
        Assert.Equal(new BindDispatch[] { new("exec", "notepad") }, decision.Dispatches);
        Assert.False(release.Consumed);
    }

    [Theory]
    [InlineData("Super_L")]
    [InlineData("Super_R")]
    [InlineData("Alt_L")]
    [InlineData("Alt_R")]
    [InlineData("Control_L")]
    [InlineData("Control_R")]
    [InlineData("Shift_L")]
    [InlineData("Shift_R")]
    public void ModifierKeyBindMatchesBeforeThatKeyJoinsTheMask(string key)
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse($"bind = , {key}, killactive\n")));
        var session = new BindSession();

        var decision = core.Decide(new KeyPressed(key), session);

        Assert.True(decision.Consumed);
        Assert.Equal("killactive", decision.Dispatcher);
        Assert.Equal(new BindDispatch[] { new("killactive", "") }, decision.Dispatches);
    }

    [Fact]
    public void ModifierKeyBindMatchesTheOtherModifiersThatAreHeld()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = SUPER, Alt_L, killactive\n")));
        var session = new BindSession();

        Assert.False(core.Decide(new KeyPressed("Alt_L"), session).Consumed);
        core.Decide(new KeyReleased("Alt_L"), session);
        core.Decide(new KeyPressed("Super_L"), session);

        var decision = core.Decide(new KeyPressed("Alt_L"), session);

        Assert.True(decision.Consumed);
        Assert.Equal("killactive", decision.Dispatcher);
    }

    [Fact]
    public void ModifierKeyBindKeepsTheModifierHeldForTheNextKey()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = , Super_L, exec, menu\nbind = SUPER, Q, killactive\n")));
        var session = new BindSession();

        var super = core.Decide(new KeyPressed("Super_L"), session);
        var next = core.Decide(new KeyPressed("Q"), session);

        Assert.Equal("exec", super.Dispatcher);
        Assert.Equal("killactive", next.Dispatcher);
        Assert.True(next.Consumed);
    }

    [Fact]
    public void OtherSideOfAModifierStaysInTheMask()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = , Super_L, killactive\nbind = SUPER, Super_L, exec, menu\n")));
        var session = new BindSession();
        core.Decide(new KeyPressed("Super_R"), session);

        var decision = core.Decide(new KeyPressed("Super_L"), session);

        Assert.Equal("exec", decision.Dispatcher);
        Assert.Equal(new BindDispatch[] { new("exec", "menu") }, decision.Dispatches);
    }

    [Fact]
    public void ModifierReleaseBindFiresWhenThatKeyGoesUp()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bindr = , Super_L, killactive\n")));
        var session = new BindSession();

        var press = core.Decide(new KeyPressed("Super_L"), session);
        var release = core.Decide(new KeyReleased("Super_L"), session);

        Assert.True(press.Consumed);
        Assert.Null(press.Dispatcher);
        Assert.Equal("killactive", release.Dispatcher);
        Assert.True(release.Consumed);
    }

    [Fact]
    public void ModifierRepeatBindFiresAgainWhileHeld()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("binde = , Super_L, killactive\n")));
        var session = new BindSession();

        Assert.Equal("killactive", core.Decide(new KeyPressed("Super_L"), session).Dispatcher);
        Assert.Equal("killactive", core.Decide(new KeyPressed("Super_L"), session).Dispatcher);
    }

    [Fact]
    public void EmptyDispatcherConsumesTheKeyAndRunsNothing()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = , Q,\n")));
        var session = new BindSession();

        var decision = core.Decide(new KeyPressed("Q"), session);

        Assert.True(decision.Consumed);
        Assert.Null(decision.Dispatcher);
        Assert.Empty(decision.Dispatches);
        Assert.True(core.Decide(new KeyReleased("Q"), session).Consumed);
    }

    [Fact]
    public void EmptyDispatcherDoesNotReplaceAnotherBindOnTheSameChord()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = , Q,\nbind = , Q, killactive\n")));
        var session = new BindSession();

        var decision = core.Decide(new KeyPressed("Q"), session);

        Assert.True(decision.Consumed);
        Assert.Equal("killactive", decision.Dispatcher);
        Assert.Equal(new BindDispatch[] { new("killactive", "") }, decision.Dispatches);
    }

    [Fact]
    public void PressBindsRunOnPressAndReleaseBindsRunOnRelease()
    {
        var core = new Core();
        core.Handle(new ConfigLoaded(ConfigParser.Parse("bind = , Q, killactive\nbindr = , Q, workspace, 2\n")));
        var session = new BindSession();

        var press = core.Decide(new KeyPressed("Q"), session);
        var release = core.Decide(new KeyReleased("Q"), session);

        Assert.Equal(new BindDispatch[] { new("killactive", "") }, press.Dispatches);
        Assert.Equal(new BindDispatch[] { new("workspace", "2") }, release.Dispatches);
    }
}
