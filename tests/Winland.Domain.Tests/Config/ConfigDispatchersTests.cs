namespace Winland.Domain.Tests;

public class ConfigDispatchersTests
{
    private static readonly string[] Known = ["killactive", "workspace"];

    private const string Binds = """
        bind = SUPER, Q, killactive
        bind = SUPER, W, killactiv
        bind = SUPER, E, teleport
        """;

    [Fact]
    public void ABindForANameOutsideTheGivenOnesWarnsOnItsLine()
    {
        var document = ConfigParser.Parse(Binds, dispatchers: Known);

        Assert.Single(document.Binds);
        Assert.Equal(
            [
                new ConfigWarning(2, "Unknown dispatcher 'killactiv'. Did you mean 'killactive'?"),
                new ConfigWarning(3, "Unknown dispatcher 'teleport'."),
            ],
            document.Warnings);
    }

    [Fact]
    public void ACaseThatDiffersStillMatches()
    {
        var document = ConfigParser.Parse("bind = SUPER, Q, KillActive", dispatchers: Known);

        Assert.Empty(document.Warnings);
        Assert.Single(document.Binds);
    }

    [Fact]
    public void WithNoNamesGivenABindIsNotChecked()
    {
        var document = ConfigParser.Parse(Binds);

        Assert.Empty(document.Warnings);
        Assert.Equal(3, document.Binds.Count);
    }
}
