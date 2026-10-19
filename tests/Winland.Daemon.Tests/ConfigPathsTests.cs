using Winland;

namespace Winland.Daemon.Tests;

public class ConfigPathsTests
{
    [Fact]
    public void TheDefaultConfigIsInAWinlandFolderUnderApplicationData()
    {
        var path = ConfigPaths.Default(@"C:\Users\me\AppData\Roaming");

        Assert.Equal(@"C:\Users\me\AppData\Roaming\winland\winland.conf", path);
    }

    [Fact]
    public void ALuaConfigIsReadInsteadWhenThereIsOne()
    {
        var data = Path.Combine(Path.GetTempPath(), "winland-config-paths-" + Guid.NewGuid().ToString("N"));
        var lua = Path.Combine(data, "winland", "winland.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(lua)!);
        try
        {
            Assert.Equal(Path.Combine(data, "winland", "winland.conf"), ConfigPaths.Default(data));
            File.WriteAllText(lua, "hl.config({})\n");
            Assert.Equal(lua, ConfigPaths.Default(data));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    // A source is read relative to the config's folder, so that is where a change to it is watched.
    [Fact]
    public void TheConfigAndEverySourceAreWatchedAtTheirFullPaths()
    {
        var watched = ConfigPaths.Watched(
            @"D:\config\winland.conf",
            ["colors.conf", @"binds\..\keys.conf", @"E:\shared\theme.conf"]);

        Assert.Equal(
            [@"D:\config\winland.conf", @"D:\config\colors.conf", @"D:\config\keys.conf", @"E:\shared\theme.conf"],
            watched);
    }

    [Fact]
    public void ARelativeConfigIsWatchedWhereItIsReadFrom()
    {
        var watched = ConfigPaths.Watched("winland.conf", ["colors.conf"]);

        Assert.Equal(
            [Path.GetFullPath("winland.conf"), Path.Combine(Environment.CurrentDirectory, "colors.conf")],
            watched);
    }

    [Fact]
    public void AConfigWithoutSourcesWatchesOnlyItself()
    {
        Assert.Equal([@"D:\config\winland.conf"], ConfigPaths.Watched(@"D:\config\winland.conf", []));
    }

    [Fact]
    public void NoConfigOrNoSourceListIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ConfigPaths.Watched(" ", []));
        Assert.Throws<ArgumentNullException>(() => ConfigPaths.Watched(@"D:\config\winland.conf", null!));
    }
}
