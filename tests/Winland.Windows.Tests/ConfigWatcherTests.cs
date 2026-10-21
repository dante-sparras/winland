using Winland.Core;
using Winland.Windows;

namespace Winland.Windows.Tests;

// The watch tests wait out real debounces, so each one is a class of its own and xUnit runs them side by side.
// They share the helpers below.
public abstract class ConfigWatcherFacts
{
    // Long enough that the several events of one rename always fall inside one debounce,
    // even on a loaded machine, so "once" does not depend on how fast events arrive.
    protected static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    protected static bool WaitUntil(Func<bool> condition)
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
    protected static int Settled(ref int count)
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

    protected static TempDir Temp() => new(Directory.CreateTempSubdirectory("winland-watch-").FullName);

    protected sealed class TempDir(string path) : IDisposable
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

public class ConfigWatcherTests : ConfigWatcherFacts
{
    [Fact]
    public void ADebounceThatIsNotPositiveIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfigWatcher(() => { }, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new ConfigWatcher(null!));
        using var watcher = new ConfigWatcher(() => { });
        Assert.Throws<ArgumentNullException>(() => watcher.Watch(null!));
        Assert.Equal(TimeSpan.FromMilliseconds(150), ConfigWatcher.DefaultDebounce);
    }

    [Fact]
    public void HostReloadAppliesTheCoreReply()
    {
        var core = new WindowManager
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
}

public class ConfigWatcherRenameTests : ConfigWatcherFacts
{
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
}

public class ConfigWatcherSourceTests : ConfigWatcherFacts
{
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
}

public class ConfigWatcherEditTests : ConfigWatcherFacts
{
    // An editor that saves in place, one that deletes and writes again, and a config that appears
    // where there was none each reload.
    [Fact]
    public void AWriteADeleteAndANewFileEachReload()
    {
        using var dir = Temp();
        var config = Path.Combine(dir.Path, "winland.conf");
        var later = Path.Combine(dir.Path, "later.conf");
        File.WriteAllText(config, "general:gaps_in = 0\n");
        var count = 0;
        using var watcher = new ConfigWatcher(() => Interlocked.Increment(ref count), Debounce);
        watcher.Watch([config, later]);

        File.AppendAllText(config, "general:gaps_in = 1\n");
        Assert.True(WaitUntil(() => Volatile.Read(ref count) > 0), "Writing the file in place did not reload.");
        var written = Settled(ref count);

        File.Delete(config);
        Assert.True(WaitUntil(() => Volatile.Read(ref count) > written), "Deleting the file did not reload.");
        var deleted = Settled(ref count);

        File.WriteAllText(later, "general:gaps_in = 2\n");
        Assert.True(WaitUntil(() => Volatile.Read(ref count) > deleted), "A new watched file did not reload.");
    }
}

public class ConfigWatcherRewatchTests : ConfigWatcherFacts
{
    // A reload can drop a source. A change to it then reloads nothing, and the new list is watched.
    [Fact]
    public void WatchingAgainReplacesTheWatchedFiles()
    {
        using var dir = Temp();
        using var other = Temp();
        var config = Path.Combine(dir.Path, "winland.conf");
        var dropped = Path.Combine(other.Path, "dropped.conf");
        File.WriteAllText(config, "");
        File.WriteAllText(dropped, "");
        var count = 0;
        using var watcher = new ConfigWatcher(() => Interlocked.Increment(ref count), Debounce);
        watcher.Watch([dropped]);
        watcher.Watch([config, " ", Path.Combine(dir.Path, "missing", "gone.conf")]);

        File.AppendAllText(dropped, "general:gaps_in = 3\n");
        var afterDropped = Settled(ref count);
        File.AppendAllText(config, "general:gaps_in = 3\n");

        Assert.Equal(0, afterDropped);
        Assert.True(WaitUntil(() => Volatile.Read(ref count) > 0), "The newly watched file did not reload.");
    }
}

public class ConfigWatcherDisposeTests : ConfigWatcherFacts
{
    [Fact]
    public void NothingReloadsAfterDispose()
    {
        using var dir = Temp();
        var config = Path.Combine(dir.Path, "winland.conf");
        File.WriteAllText(config, "");
        var count = 0;
        var watcher = new ConfigWatcher(() => Interlocked.Increment(ref count), Debounce);
        watcher.Watch([config]);

        watcher.Dispose();
        watcher.Watch([config]);
        File.AppendAllText(config, "general:gaps_in = 1\n");
        watcher.Dispose();

        Assert.Equal(0, Settled(ref count));
    }
}

public class ConfigWatcherThrowTests : ConfigWatcherFacts
{
    [Fact]
    public void AReloadThatThrowsDoesNotStopTheWatch()
    {
        using var dir = Temp();
        var config = Path.Combine(dir.Path, "winland.conf");
        File.WriteAllText(config, "");
        var count = 0;
        using var watcher = new ConfigWatcher(
            () =>
            {
                Interlocked.Increment(ref count);
                throw new InvalidOperationException("The config could not be read.");
            },
            Debounce);
        watcher.Watch([config]);

        File.AppendAllText(config, "a\n");
        Assert.True(WaitUntil(() => Volatile.Read(ref count) > 0), "The first change did not reload.");
        var first = Settled(ref count);
        File.AppendAllText(config, "b\n");

        Assert.True(WaitUntil(() => Volatile.Read(ref count) > first), "The watch stopped after a reload threw.");
    }
}
