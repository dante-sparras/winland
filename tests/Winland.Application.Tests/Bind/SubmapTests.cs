namespace Winland.Application.Tests;

public class SubmapTests
{
    [Fact]
    public void EnteringASubmapSwapsTheActiveBindSet()
    {
        var core = Load(
            "bind = , R, submap, resize\n" +
            "bind = , Q, killactive\n" +
            "submap = resize\n" +
            "bind = , H, killactive\n" +
            "bind = , escape, submap, reset\n" +
            "submap = reset\n");
        Appear(core, 1);

        AssertPassed(Tap(core, "H"));
        var entered = Tap(core, "R");
        Assert.Equal("submap", entered.Dispatcher);
        Assert.Equal(["submap>>resize"], Lines(entered));

        AssertPassed(Tap(core, "Q"));
        AssertRan(Tap(core, "H"), "killactive", 1);
    }

    [Fact]
    public void ResetReturnsToTheDefaultMap()
    {
        var core = Load(
            "bind = , R, submap, resize\n" +
            "bind = , Q, killactive\n" +
            "submap = resize\n" +
            "bind = , H, killactive\n" +
            "bind = , escape, submap, reset\n" +
            "submap = reset\n");
        Appear(core, 1);
        Tap(core, "R");

        var left = Tap(core, "Escape");

        Assert.Equal("submap", left.Dispatcher);
        Assert.Equal(["submap>>"], Lines(left));
        AssertPassed(Tap(core, "H"));
        AssertRan(Tap(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void MatcherReadsTheActiveSubmapFromTheSession()
    {
        var core = Load(
            "bind = , Q, killactive\n" +
            "submap = resize\n" +
            "bind = , H, killactive\n" +
            "submap = reset\n");
        var session = new BindSession { Submap = "resize" };

        var decision = core.Decide(new KeyPressed("H"), session);

        Assert.Equal("killactive", decision.Dispatcher);
        Assert.Null(core.Decide(new KeyPressed("Q"), session).Dispatcher);
    }

    [Fact]
    public void UniversalBindsStayActiveInEverySubmap()
    {
        var core = Load(
            "bindu = , Q, killactive\n" +
            "bind = , E, submap, resize\n" +
            "submap = other\n" +
            "bindu = , W, killactive\n" +
            "submap = reset\n" +
            "submap = resize\n" +
            "bind = , H, killactive\n" +
            "submap = reset\n");
        Appear(core, 1);

        AssertRan(Tap(core, "Q"), "killactive", 1);
        AssertRan(Tap(core, "W"), "killactive", 1);
        Tap(core, "E");

        AssertRan(Tap(core, "Q"), "killactive", 1);
        AssertRan(Tap(core, "W"), "killactive", 1);
        AssertRan(Tap(core, "H"), "killactive", 1);
        AssertPassed(Tap(core, "E"));
    }

    [Fact]
    public void CatchallRunsOnlyWhenNothingElseInTheSubmapMatched()
    {
        var core = Load(
            "bind = , R, submap, resize\n" +
            "bind = , Q, killactive\n" +
            "submap = resize\n" +
            "bind = , H, killactive\n" +
            "bind = , catchall, submap, reset\n" +
            "submap = reset\n");
        Appear(core, 1);
        Tap(core, "R");

        var matched = Tap(core, "H");
        Assert.Equal("killactive", matched.Dispatcher);
        Assert.Empty(Lines(matched));
        AssertRan(Tap(core, "H"), "killactive", 1);

        var missed = Tap(core, "Q");
        Assert.Equal("submap", missed.Dispatcher);
        Assert.Equal(["submap>>"], Lines(missed));
        AssertRan(Tap(core, "Q"), "killactive", 1);
        AssertPassed(Tap(core, "Z"));
    }

    [Fact]
    public void CatchallDoesNotRunOnReleaseAfterTheSubmapChanged()
    {
        var core = Load(
            "bind = , R, submap, resize\n" +
            "submap = resize\n" +
            "bind = , X, submap, reset\n" +
            "bindr = , catchall, killactive\n" +
            "submap = reset\n");
        Appear(core, 1);
        Press(core, "R");
        Press(core, "X");

        var released = Release(core, "X");

        Assert.Null(released.Dispatcher);
        Assert.Null(released.Close);
    }

    [Fact]
    public void CatchallOnReleaseFiresWhileTheSubmapStays()
    {
        var core = Load(
            "bind = , R, submap, resize\n" +
            "submap = resize\n" +
            "bindr = , catchall, killactive\n" +
            "submap = reset\n");
        Appear(core, 1);
        Press(core, "R");
        Assert.Null(Press(core, "Z").Dispatcher);

        AssertRan(Release(core, "Z"), "killactive", 1);
    }

    [Fact]
    public void ASubmapDispatcherStopsLaterBindsOnThatKey()
    {
        var core = Load(
            "bind = , R, submap, resize\n" +
            "submap = resize\n" +
            "bind = , H, killactive\n" +
            "bind = , H, submap, reset\n" +
            "bind = , H, workspace, 2\n" +
            "submap = reset\n");
        Appear(core, 1);
        Press(core, "R");

        var outcome = Press(core, "H");

        Assert.Equal(new WindowId(1), outcome.Close);
        Assert.Equal(WorkspaceId.Initial, outcome.State.ActiveWorkspace);
        Assert.Equal(["submap>>"], Lines(outcome));
    }

    [Fact]
    public void NestedSubmapResetReturnsToTheDefaultNotTheParent()
    {
        var core = Load(
            "bind = , A, submap, alpha\n" +
            "submap = alpha\n" +
            "bind = , B, submap, beta\n" +
            "bind = , P, killactive\n" +
            "submap = reset\n" +
            "submap = beta\n" +
            "bind = , P, killactive\n" +
            "bind = , escape, submap, reset\n" +
            "submap = reset\n" +
            "bind = , Q, killactive\n");
        Appear(core, 1);
        Tap(core, "A");
        var nested = Tap(core, "B");
        Assert.Equal(["submap>>beta"], Lines(nested));
        AssertPassed(Tap(core, "Q"));
        AssertRan(Tap(core, "P"), "killactive", 1);

        var left = Tap(core, "Escape");

        Assert.Equal(["submap>>"], Lines(left));
        AssertPassed(Tap(core, "P"));
        AssertRan(Tap(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void ABindCanReturnToTheSubmapNamedAsItsReset()
    {
        var core = Load(
            "bind = , A, submap, alpha\n" +
            "submap = alpha\n" +
            "bind = , B, submap, beta\n" +
            "bind = , P, killactive\n" +
            "submap = reset\n" +
            "submap = beta, alpha\n" +
            "bind = , H, killactive\n" +
            "submap = reset\n");
        Appear(core, 1);
        Tap(core, "A");
        Tap(core, "B");

        var outcome = Tap(core, "H");

        Assert.Equal("killactive", outcome.Dispatcher);
        Assert.Equal(["submap>>alpha"], Lines(outcome));
        AssertRan(Tap(core, "P"), "killactive", 1);
        AssertPassed(Tap(core, "H"));
    }

    [Fact]
    public void AutoResetLeavesAfterABindThatIsNotTheSubmapDispatcher()
    {
        var core = Load(
            "bind = , R, submap, resize\n" +
            "bind = , Q, killactive\n" +
            "submap = resize, reset\n" +
            "bind = , H, killactive\n" +
            "bind = , N, submap, nested\n" +
            "submap = reset\n" +
            "submap = nested\n" +
            "bind = , H, killactive\n" +
            "submap = reset\n");
        Appear(core, 1);
        Tap(core, "R");

        var resized = Tap(core, "H");
        Assert.Equal("killactive", resized.Dispatcher);
        Assert.Equal(["submap>>"], Lines(resized));
        AssertRan(Tap(core, "Q"), "killactive", 1);

        Tap(core, "R");
        var nested = Tap(core, "N");
        Assert.Equal(["submap>>nested"], Lines(nested));
        AssertRan(Tap(core, "H"), "killactive", 1);
    }

    [Fact]
    public void UnknownSubmapDoesNotChangeTheActiveMap()
    {
        var core = Load(
            "bind = , R, submap, missing\n" +
            "bind = , Q, killactive\n");
        Appear(core, 1);

        var outcome = Tap(core, "R");

        Assert.Equal("error: submap 'missing' does not exist", outcome.Error);
        Assert.Empty(outcome.Events);
        AssertRan(Tap(core, "Q"), "killactive", 1);
    }

    [Fact]
    public void DispatchEmitsTheSubmapEvent()
    {
        var core = Load("submap = resize\nbind = , H, killactive\nsubmap = reset\n");

        var entered = core.Execute("dispatch submap resize");
        var again = core.Execute("dispatch submap RESET");
        var left = core.Execute("dispatch submap");

        Assert.Equal("ok", entered.Text);
        Assert.Equal(["submap>>resize"], Lines(entered));
        Assert.Equal("error: submap 'RESET' does not exist", again.Text);
        Assert.Empty(again.Effect.Events);
        Assert.Equal(["submap>>"], Lines(left));
        AssertPassed(Tap(core, "H"));
    }

    [Fact]
    public void CatchallOutsideASubmapWarnsAndIsSkipped()
    {
        var document = ConfigParser.Parse("bind = , catchall, killactive\nbind = , Q, killactive\n");

        Assert.Equal(
            new ConfigWarning(1, "Catchall binds are only allowed in a submap."),
            Assert.Single(document.Warnings));
        Assert.Equal(
            new Bind(Modifier.None, "Q", "killactive", "", BindFlag.None, null, null),
            Assert.Single(document.Binds));
    }

    private static void Appear(WindowManager core, long window) =>
        core.Handle(new WindowAppeared(new WindowId(window), new Rectangle(0, 0, 1920, 1080)));

    private static Outcome Press(WindowManager core, string key) =>
        core.Handle(new KeyPressed(key));

    private static Outcome Release(WindowManager core, string key) =>
        core.Handle(new KeyReleased(key));

    private static Outcome Tap(WindowManager core, string key)
    {
        var outcome = Press(core, key);
        Release(core, key);
        return outcome;
    }

    private static IReadOnlyList<string> Lines(Outcome outcome) =>
        CommandProtocol.FormatStream(outcome.Events, outcome.State);

    private static IReadOnlyList<string> Lines(CommandReply reply) =>
        Lines(reply.Effect);

    private static void AssertPassed(Outcome outcome)
    {
        Assert.False(outcome.Consumed);
        Assert.Null(outcome.Dispatcher);
        Assert.Null(outcome.Close);
    }

    private static void AssertRan(Outcome outcome, string dispatcher, long close)
    {
        Assert.True(outcome.Consumed);
        Assert.Equal(dispatcher, outcome.Dispatcher);
        Assert.Equal(new WindowId(close), outcome.Close);
    }
}
