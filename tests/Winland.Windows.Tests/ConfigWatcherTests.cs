using Winland;
using Winland.Windows;

namespace Winland.Windows.Tests;

public class ConfigWatcherTests
{
    // Long enough that the several events of one rename always fall inside one debounce,
    // even on a loaded machine, so "once" does not depend on how fast events arrive.
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void RenamingOverAWatchedFileReloadsOnce()
    {
        using var dir = Temp();
        var config = Path.Combine(dir.Path, "winland.conf");
        var notes = Path.Combine(dir.Path, "notes.txt");
        File.WriteAllText(config, "general:gaps_in = 0\n");
        File.WriteAllText(notes, "");
        var count = 0;
        using var watcher = new ConfigWatcher(() => Interlocked.Increment(ref count), Debounce);
        watcher.Watch([config]);

        // A second watcher on the unrelated file tells when its change has been seen and debounced.
        var seen = 0;
        using (var probe = new ConfigWatcher(() => Interlocked.Increment(ref seen), Debounce))
        {
            probe.Watch([notes]);
            File.WriteAllText(notes, "leave this");
            Assert.True(WaitUntil(() => Volatile.Read(ref seen) > 0), "The change to the unrelated file was never seen.");
        }

        Assert.Equal(0, Settled(ref count));

        var replaced = Path.Combine(dir.Path, "winland.conf.new");
        File.WriteAllText(replaced, "blur = yes\n");
        File.Move(replaced, config, overwrite: true);

        Assert.True(WaitUntil(() => Volatile.Read(ref count) > 0), "The rename did not reload.");
        Assert.Equal(1, Settled(ref count));
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
        using var watcher = new ConfigWatcher(() => Interlocked.Increment(ref count), Debounce);
        watcher.Watch([config, source]);

        var replaced = Path.Combine(dir.Path, "extra.conf.new");
        File.WriteAllText(replaced, "general:gaps_in = 4\n");
        File.Move(replaced, source, overwrite: true);

        Assert.True(WaitUntil(() => Volatile.Read(ref count) > 0), "The sourced file change did not reload.");
        Assert.Equal(1, Settled(ref count));
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

    private static bool WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            Thread.Sleep(20);
        }

        return condition();
    }

    // The count once it has stayed the same for three debounce periods, so a late
    // reload that a short fixed sleep would miss is counted.
    private static int Settled(ref int count)
    {
        var quiet = Debounce * 3;
        var value = Volatile.Read(ref count);
        var since = DateTime.UtcNow;
        while (DateTime.UtcNow - since < quiet)
        {
            Thread.Sleep(20);
            var now = Volatile.Read(ref count);
            if (now != value)
            {
                value = now;
                since = DateTime.UtcNow;
            }
        }

        return value;
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
