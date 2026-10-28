namespace Winland.Daemon.Tests;

public sealed class ShellShortcutTests : IDisposable
{
    private readonly string link = Path.Combine(Path.GetTempPath(), "winland-shortcut-" + Guid.NewGuid().ToString("N") + ".lnk");

    public void Dispose()
    {
        if (File.Exists(link))
            File.Delete(link);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Winland\winland.exe")]
    [InlineData(@"C:\Program Files\Winland2\winland.exe")]
    public void TheShortcutCarriesTheAppUserModelId(string target)
    {
        ShellShortcut.Write(
            link,
            target,
            "--no-elevate",
            "Winland.Winland");

        // A shortcut is binary; its UTF-16 property value can start at an odd byte offset.
        var bytes = File.ReadAllBytes(link);
        var appUserModelId = System.Text.Encoding.Unicode.GetBytes("Winland.Winland");
        Assert.True(bytes.AsSpan().IndexOf(appUserModelId) >= 0, "The shortcut must contain the AppUserModelID.");
    }
}
