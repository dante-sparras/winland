using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class ConfigWatcherTests
{
    [Fact]
    public void RenamingOverAWatchedFileReloadsOnce()
    {
        using var dir = Temp();
        var config = Path.Combine(dir.Path, "winland.conf");
        File.WriteAllText(config, "general:gaps_in = 0\n");
        var count = 0;
        using var watcher = new ConfigWatcher(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(80));
        watcher.Watch([config]);

        File.WriteAllText(Path.Combine(dir.Path, "notes.txt"), "leave this");
        Thread.Sleep(250);
        Assert.Equal(0, Volatile.Read(ref count));

        var replaced = Path.Combine(dir.Path, "winland.conf.new");
        File.WriteAllText(replaced, "blur = yes\n");
        File.Move(replaced, config, overwrite: true);

        Assert.Equal(1, WaitFor(ref count, 1));
    }

    [Fact]
    public void ASourcedFileChangeReloads()
    {
        using var dir = Temp();
        var config = Path.Combine(dir.Path, "winland.conf");
        var source = Path.Combine(dir.Path, "extra.conf");
        File.WriteAllText(config, "source = extra.conf\n");
        File.WriteAllText(source, "general:gaps_in = 0\n");
        var count = 0;
        using var watcher = new ConfigWatcher(() => Interlocked.Increment(ref count), TimeSpan.FromMilliseconds(80));
        watcher.Watch([config, source]);

        var replaced = Path.Combine(dir.Path, "extra.conf.new");
        File.WriteAllText(replaced, "general:gaps_in = 4\n");
        File.Move(replaced, source, overwrite: true);

        Assert.Equal(1, WaitFor(ref count, 1));
    }

    [Fact]
    public void HostReloadAppliesTheCoreReply()
    {
        var core = new Core
        {
            ReadConfig = () => new ConfigRead(ConfigParser.Parse("blur = on\n")),
        };
        using var host = new WindowHost(core);

        var reply = host.Reload();

        Assert.Equal("ok", reply.Text);
        Assert.Equal(
            ["warning>>1,,Unsupported keyword 'blur'.", "configreloaded>>"],
            CommandProtocol.FormatStream(reply.Effect.Events, reply.Effect.State));
    }

    private static int WaitFor(ref int count, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var seen = Volatile.Read(ref count);
            if (seen >= expected)
            {
                Thread.Sleep(250);
                return Volatile.Read(ref count);
            }

            Thread.Sleep(20);
        }

        return Volatile.Read(ref count);
    }

    private static TempDir Temp() => new(Directory.CreateTempSubdirectory("winland-watch-").FullName);

    private sealed class TempDir(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
