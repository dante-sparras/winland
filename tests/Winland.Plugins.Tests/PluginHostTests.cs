using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Winland.Plugins;

namespace Winland.Tests;

[Collection("Winland.Plugins")]
public class PluginHostTests
{
    [Fact]
    public void ApprovedPluginLoadsAndItsDispatcherRuns()
    {
        using var fixture = Fixture.Create("sample");
        var core = new Core { Plugins = fixture.Host };

        Assert.Equal("ok", core.Execute("plugin load " + fixture.AssemblyPath).Text);

        var reply = core.Execute("dispatch sampleecho ping");

        Assert.Equal("ping", reply.Text);
        Assert.Equal("sampleecho", reply.Effect.Dispatcher);
        Assert.Null(reply.Effect.Error);
        Assert.Equal("ping", reply.Effect.Notice);
    }

    [Fact]
    public void UnapprovedPluginDoesNotLoad()
    {
        using var fixture = Fixture.Create();
        File.WriteAllText(fixture.ApprovalsPath, """[{"name":"sample","sha256":"0000"}]""");
        var core = new Core { Plugins = fixture.Host };

        var reply = core.Execute("plugin load " + fixture.AssemblyPath);

        Assert.StartsWith("error:", reply.Text, StringComparison.Ordinal);
        Assert.Contains("not approved", reply.Text, StringComparison.Ordinal);
        Assert.Contains("unknown dispatcher", core.Execute("dispatch sampleecho ping").Text, StringComparison.Ordinal);
        Assert.Equal("[]", core.Execute("plugin list").Text);
    }

    [Fact]
    public void ChangedPluginDoesNotLoad()
    {
        using var fixture = Fixture.Create("sample");
        var core = new Core { Plugins = fixture.Host };
        Assert.Equal("ok", core.Execute("plugin load " + fixture.AssemblyPath).Text);
        Assert.Equal("ok", core.Execute("plugin unload sample").Text);

        var bytes = File.ReadAllBytes(fixture.AssemblyPath);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(fixture.AssemblyPath, bytes);

        var reply = core.Execute("plugin load " + fixture.AssemblyPath);

        Assert.Contains("not approved", reply.Text, StringComparison.Ordinal);
        Assert.False(Dispatchers.IsKnown("sampleecho"));
    }

    [Fact]
    public void ApiVersionMismatchIsRefused()
    {
        using var fixture = Fixture.Create("mismatch");
        var core = new Core { Plugins = fixture.Host };

        var reply = core.Execute("plugin load " + fixture.AssemblyPath);

        Assert.Contains("API version", reply.Text, StringComparison.Ordinal);
        Assert.Contains("Winland accepts 1", reply.Text, StringComparison.Ordinal);
        Assert.Equal("[]", core.Execute("plugin list").Text);
    }

    [Fact]
    public void ThrowingPluginIsContainedAndDisabledAfterThreeFailures()
    {
        using var fixture = Fixture.Create("faulty");
        var core = new Core { Plugins = fixture.Host };
        Assert.Equal("ok", core.Execute("plugin load " + fixture.AssemblyPath).Text);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var failed = core.Execute("dispatch boom");
            Assert.StartsWith("error:", failed.Text, StringComparison.Ordinal);
            Assert.Contains("boom", failed.Text, StringComparison.Ordinal);
            Assert.Null(failed.Effect.Close);
        }

        var disabled = core.Execute("dispatch boom");
        Assert.Contains("disabled", disabled.Text, StringComparison.Ordinal);
        using var clients = JsonDocument.Parse(core.Execute("clients").Text);
        Assert.Equal(JsonValueKind.Array, clients.RootElement.ValueKind);

        var listed = JsonDocument.Parse(core.Execute("plugin list").Text).RootElement;
        var plugin = Assert.Single(listed.EnumerateArray());
        Assert.Equal("faulty", plugin.GetProperty("name").GetString());
        Assert.Equal(1, plugin.GetProperty("version").GetInt32());
        Assert.Equal("disabled", plugin.GetProperty("state").GetString());
        Assert.Equal(3, plugin.GetProperty("failures").GetInt32());
        Assert.Contains(fixture.Log.Lines, line => line.Contains("disabled after 3", StringComparison.Ordinal));
    }

    [Fact]
    public void PluginLoadSurvivesASaturatedThreadPool()
    {
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIo);
        Assert.True(ThreadPool.SetMaxThreads(workers, maxIo));
        var gate = new ManualResetEventSlim(false);
        var running = 0;
        try
        {
            for (var i = 0; i < workers; i++)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Interlocked.Increment(ref running);
                    gate.Wait();
                });
            }

            var started = Stopwatch.StartNew();
            var available = int.MaxValue;
            while (started.Elapsed < TimeSpan.FromSeconds(5))
            {
                ThreadPool.GetAvailableThreads(out available, out _);
                if (available == 0 && Volatile.Read(ref running) > 0)
                    break;

                Thread.Sleep(1);
            }

            Assert.Equal(0, available);

            using var fixture = Fixture.Create("sample");
            Assert.Equal("ok", fixture.Host.Load(fixture.AssemblyPath));
        }
        finally
        {
            gate.Set();
            ThreadPool.SetMaxThreads(maxWorkers, maxIo);
            ThreadPool.SetMinThreads(workers, io);
        }
    }

    [Fact]
    public void HangingPluginIsAbandonedWithinTheBudget()
    {
        using var fixture = Fixture.Create(TimeSpan.FromMilliseconds(50), "hanger");
        var core = new Core { Plugins = fixture.Host };
        Assert.Equal("ok", core.Execute("plugin load " + fixture.AssemblyPath).Text);

        var watch = Stopwatch.StartNew();
        var reply = core.Execute("dispatch hang");
        watch.Stop();

        Assert.Contains("time budget", reply.Text, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"abandoned call took {watch.Elapsed}");
        Assert.Contains(fixture.Log.Lines, line => line.Contains("abandoned", StringComparison.Ordinal));
        using var clients = JsonDocument.Parse(core.Execute("clients").Text);
        Assert.Equal(JsonValueKind.Array, clients.RootElement.ValueKind);
    }

    [Fact]
    public void UnloadRemovesRegistrationsAndTheContextIsCollectible()
    {
        var weak = LoadAndUnload();
        for (var attempt = 0; weak.IsAlive && attempt < 20; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive);
        Assert.False(Dispatchers.IsKnown("sampleecho"));
    }

    [Fact]
    public void WritableLocationRefusesToLoad()
    {
        using var fixture = Fixture.Create("sample");
        fixture.Host.Dispose();
        var log = new ListLog();
        using var host = new PluginHost(
            new DenyGuard("Refusing to load plugins because a non-administrator (S-1-5-32-545) can write the plugin directory."),
            log,
            fixture.Options);
        var core = new Core { Plugins = host };

        var reply = core.Execute("plugin load " + fixture.AssemblyPath);

        Assert.Contains("non-administrator", reply.Text, StringComparison.Ordinal);
        Assert.Contains(log.Lines, line => line.Contains("non-administrator", StringComparison.Ordinal));
        Assert.False(Dispatchers.IsKnown("sampleecho"));
    }

    [Fact]
    public void PluginKeywordLoadsOnConfigAndTheKeywordHandlerRuns()
    {
        using var fixture = Fixture.Create("sample");
        var text = "plugin = " + fixture.AssemblyPath + "\nsamplekeyword = hello\nsamplekeyword = bad\n";
        var document = fixture.Host.Incorporate(ConfigParser.Parse(text), () => ConfigParser.Parse(text));

        Assert.Contains(document.Settings, setting => setting.Keyword == "plugin");
        Assert.Contains(document.Settings, setting => setting.Keyword == "samplekeyword" && setting.Value == "hello");
        Assert.Contains(document.Warnings, warning => warning.Message.Contains("sample keyword rejected 'bad'", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Warnings, warning => warning.Message.Contains("Unsupported keyword 'samplekeyword'", StringComparison.Ordinal));

        var core = new Core { Plugins = fixture.Host };
        Assert.Equal("ping", core.Execute("dispatch sampleecho ping").Text);
    }

    [Fact]
    public void PipeListsLoadsAndUnloads()
    {
        using var fixture = Fixture.Create("sample");
        var core = new Core { Plugins = fixture.Host };

        Assert.Equal("[]", core.Execute("plugin list").Text);
        Assert.Equal("ok", core.Execute("plugin load " + fixture.AssemblyPath).Text);

        var listed = JsonDocument.Parse(core.Execute("plugin list").Text).RootElement;
        var plugin = Assert.Single(listed.EnumerateArray());
        Assert.Equal("sample", plugin.GetProperty("name").GetString());
        Assert.Equal(1, plugin.GetProperty("version").GetInt32());
        Assert.Equal("loaded", plugin.GetProperty("state").GetString());
        Assert.Equal(0, plugin.GetProperty("failures").GetInt32());

        Assert.Equal("ok", core.Execute("plugin unload sample").Text);
        Assert.Equal("[]", core.Execute("plugin list").Text);
        Assert.Contains("not loaded", core.Execute("plugin unload sample").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginCommandsWithoutAHostAreRejected()
    {
        var core = new Core();

        Assert.Equal("error: plugins are not available", core.Execute("plugin list").Text);
        Assert.Equal("error: missing plugin path", core.Execute("plugin load").Text);
        Assert.Equal("error: missing plugin name", core.Execute("plugin unload").Text);
        Assert.Equal("error: unknown request", core.Execute("plugin nonsense").Text);
    }

    [Fact]
    public void CallBudgetDefaultsTo250Milliseconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), PluginHost.DefaultCallBudget);
    }

    [Fact]
    public void EventHandlerRuns()
    {
        using var fixture = Fixture.Create("sample");
        Assert.Equal("ok", fixture.Host.Load(fixture.AssemblyPath));

        fixture.Host.Deliver([new ConfigReloaded()]);

        Assert.Equal(1, fixture.Host.EventDeliveries);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnload()
    {
        using var fixture = Fixture.Create("sample");
        Assert.Equal("ok", fixture.Host.Load(fixture.AssemblyPath));
        var weak = fixture.Host.LoadContextReference("sample");
        Assert.NotNull(weak);
        Assert.Equal("ok", fixture.Host.Unload("sample"));
        Assert.False(Dispatchers.IsKnown("sampleecho"));
        var parsed = ConfigParser.Parse("samplekeyword = hello\n");
        Assert.Contains(parsed.Warnings, warning => warning.Message.Contains("Unsupported keyword 'samplekeyword'", StringComparison.Ordinal));
        return weak;
    }

    [Fact]
    public void ApprovedAssemblyOutsideThePluginDirectoryDoesNotLoad()
    {
        using var fixture = Fixture.Create("sample");
        var outside = Path.Combine(Path.GetTempPath(), "winland-outside-" + Guid.NewGuid().ToString("N") + ".dll");
        File.Copy(fixture.AssemblyPath, outside);
        try
        {
            var reply = fixture.Host.Load(outside);

            Assert.StartsWith("error:", reply, StringComparison.Ordinal);
            Assert.Contains("outside the plugin directory", reply, StringComparison.Ordinal);
            Assert.False(Dispatchers.IsKnown("sampleecho"));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string directory, string assemblyPath, string approvalsPath, PluginHost host, PluginHostOptions options, ListLog log)
        {
            Directory = directory;
            AssemblyPath = assemblyPath;
            ApprovalsPath = approvalsPath;
            Host = host;
            Options = options;
            Log = log;
        }

        public string Directory { get; }

        public string AssemblyPath { get; }

        public string ApprovalsPath { get; }

        public PluginHost Host { get; }

        public PluginHostOptions Options { get; }

        public ListLog Log { get; }

        public static Fixture Create(params string[] names) => Create(budget: null, names);

        public static Fixture Create(TimeSpan? budget, params string[] names)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winland-plugin-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var assemblyPath = Path.Combine(directory, "SamplePlugin.dll");
            File.Copy(SampleDll(), assemblyPath);
            var approvalsPath = Path.Combine(directory, "approved.json");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))).ToLowerInvariant();
            var entries = names.Select(name => new { name, sha256 = hash }).ToArray();
            File.WriteAllText(approvalsPath, JsonSerializer.Serialize(entries));
            var log = new ListLog();
            var options = new PluginHostOptions
            {
                Directory = directory,
                ApprovalsPath = approvalsPath,
                CallBudget = budget,
            };
            var host = new PluginHost(new AllowGuard(), log, options);
            return new Fixture(directory, assemblyPath, approvalsPath, host, options, log);
        }

        public void Dispose()
        {
            Host.Dispose();
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static string SampleDll()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "SamplePlugin.dll");
            if (!File.Exists(path))
                throw new FileNotFoundException("The sample plugin was not built.", path);

            return path;
        }
    }

    private sealed class AllowGuard : IPluginLocationGuard
    {
        public string? Deny(string pluginDirectory, string approvalsFile) => null;
    }

    private sealed class DenyGuard(string message) : IPluginLocationGuard
    {
        public string? Deny(string pluginDirectory, string approvalsFile) => message;
    }

    private sealed class ListLog : IPluginLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string message) => Lines.Add(message);
    }
}

[CollectionDefinition("Winland.Plugins", DisableParallelization = true)]
public sealed class PluginTestCollection;
