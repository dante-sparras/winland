namespace Winland.Daemon.Tests;

public sealed class ConfigFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winland-config-file-" + Guid.NewGuid().ToString("N"));

    public ConfigFileTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ABindForAnUnknownDispatcherWarnsWithItsLineAndTheNameItIsCloseTo()
    {
        var path = Path.Combine(_root, "winland.conf");
        File.WriteAllText(path, "bind = SUPER, Q, killactive\nbind = SUPER, W, killactiv\n");

        var document = ConfigFile.Read(path);

        Assert.Single(document.Binds);
        var warning = Assert.Single(document.Warnings);
        Assert.Equal(2, warning.Line);
        Assert.Equal("Unknown dispatcher 'killactiv'. Did you mean 'killactive'?", warning.Message);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
