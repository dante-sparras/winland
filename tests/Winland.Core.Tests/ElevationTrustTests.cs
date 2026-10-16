using Winland;

namespace Winland.Tests;

// [elevated] starts a program with Winland's administrator token. Only a config that only
// administrators can write may ask for that (ADR 0007); the daemon decides and marks the document.
public class ElevationTrustTests
{
    private const string Requests =
        "exec = [elevated] every\n" +
        "exec-once = [elevated] once\n" +
        "exec-shutdown = [float; elevated] bye\n" +
        "bind = SUPER, E, exec, [elevated] bound\n" +
        "workspace = 3, persistent:true, on-created-empty:[elevated] born\n";

    [Fact]
    public void AParsedDocumentIsNotTrusted()
    {
        Assert.False(ConfigParser.Parse("exec = [elevated] x\n").ElevationTrusted);
    }

    [Fact]
    public void AnUnprotectedConfigLaunchesEveryRequestDeElevated()
    {
        var document = ConfigParser.Parse(Requests);
        var core = new Core();

        var created = core.Handle(new Configure(document));
        core.Handle(new KeyPressed("Super_L"));
        var bound = core.Handle(new KeyPressed("E"));

        Assert.All(core.StartupLaunches(document), launch => Assert.False(launch.Elevated));
        Assert.All(core.ReloadLaunches(document), launch => Assert.False(launch.Elevated));
        Assert.All(core.ShutdownLaunches(document), launch => Assert.False(launch.Elevated));
        Assert.Equal("born", created.Launch?.CommandLine);
        Assert.False(created.Launch?.Elevated);
        Assert.Equal("bound", bound.Launch?.CommandLine);
        Assert.False(bound.Launch?.Elevated);
        Assert.False(bound.AllowsElevation);
    }

    [Fact]
    public void AProtectedConfigLaunchesEveryRequestElevated()
    {
        var document = ConfigParser.Parse(Requests) with { ElevationTrusted = true };
        var core = new Core();

        var created = core.Handle(new Configure(document));
        core.Handle(new KeyPressed("Super_L"));
        var bound = core.Handle(new KeyPressed("E"));

        Assert.All(core.StartupLaunches(document), launch => Assert.True(launch.Elevated));
        Assert.All(core.ReloadLaunches(document), launch => Assert.True(launch.Elevated));
        Assert.All(core.ShutdownLaunches(document), launch => Assert.True(launch.Elevated));
        Assert.True(created.Launch?.Elevated);
        Assert.True(bound.Launch?.Elevated);
    }

    // keyword runs on the pipe, which never launches elevated, so it may not add a bind that would.
    [Theory]
    [InlineData("SUPER, X, exec, [elevated] cmd")]
    [InlineData("SUPER, X, execr, [float; elevated] cmd")]
    public void AKeywordCannotAddAnElevatedBind(string value)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("") with { ElevationTrusted = true }));

        var reply = core.Execute("keyword bind " + value);
        core.Handle(new KeyPressed("Super_L"));
        var pressed = core.Handle(new KeyPressed("X"));

        Assert.StartsWith("error:", reply.Text, StringComparison.Ordinal);
        Assert.Contains("elevated", reply.Text, StringComparison.Ordinal);
        Assert.Null(pressed.Launch);
    }

    [Fact]
    public void AKeywordKeepsTheTrustOfTheConfigItChanges()
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse("bind = SUPER, E, exec, [elevated] bound\n") with { ElevationTrusted = true }));

        Assert.Equal("ok", core.Execute("keyword bind SUPER, X, exec, plain").Text);
        core.Handle(new KeyPressed("Super_L"));
        var bound = core.Handle(new KeyPressed("E"));

        Assert.True(bound.Launch?.Elevated);
    }

    [Fact]
    public void AnUnprotectedConfigThatAsksWarnsWhy()
    {
        var document = ConfigElevation.Decide(ConfigParser.Parse(Requests), "a non-administrator (S-1-5-21-9) can write 'C:\\c\\winland.conf'");

        Assert.False(document.ElevationTrusted);
        var warning = Assert.Single(document.Warnings);
        Assert.Contains("[elevated]", warning.Message, StringComparison.Ordinal);
        Assert.Contains("de-elevated", warning.Message, StringComparison.Ordinal);
        Assert.Contains("S-1-5-21-9", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("exec = notepad\nbind = SUPER, E, exec, notepad\n")]
    [InlineData("exec = elevated notepad\n")]
    public void AnUnprotectedConfigThatDoesNotAskDoesNotWarn(string text)
    {
        var document = ConfigElevation.Decide(ConfigParser.Parse(text), "a non-administrator can write it");

        Assert.False(document.ElevationTrusted);
        Assert.Empty(document.Warnings);
    }

    [Fact]
    public void AProtectedConfigIsTrusted()
    {
        var document = ConfigElevation.Decide(ConfigParser.Parse(Requests), refusal: null);

        Assert.True(document.ElevationTrusted);
        Assert.Empty(document.Warnings);
    }
}
