using Winland.Core;

namespace Winland.Daemon.Tests;

public sealed class ShellShortcutTests : IDisposable
{
    private readonly string link = Path.Combine(Path.GetTempPath(), "winland-shortcut-" + Guid.NewGuid().ToString("N") + ".lnk");

    public void Dispose()
    {
        if (File.Exists(link))
            File.Delete(link);
    }

    [Fact]
    public void TheShortcutCarriesTheAppUserModelId()
    {
        ShellShortcut.Write(
            link,
            @"C:\Program Files\Winland\winland.exe",
            "--no-elevate",
            "Winland.Winland");

        Assert.Equal("Winland.Winland", ShellShortcut.ReadAppUserModelId(link));
    }
}