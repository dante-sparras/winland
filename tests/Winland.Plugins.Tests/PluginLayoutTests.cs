using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Winland.Plugins;
using Winland.SamplePlugin;

namespace Winland.Tests;

[Collection("Winland.Plugins")]
public class PluginLayoutTests
{
    private static readonly Rectangle Area = new(0, 0, 1920, 1080);

    private const string Flat = """
        general:gaps_in = 0
        general:gaps_out = 0
        general:border_size = 0

        """;

    [Fact]
    public void APluginLayoutStacksWindowsAndTheConfigIsReadAgainForItsName()
    {
        using var fixture = Fixture.Create("rows");
        var text = Flat + "plugin = " + fixture.AssemblyPath + "\ngeneral:layout = rows\n";
        var loaded = fixture.Host.Incorporate(ConfigParser.Parse(text), () => ConfigParser.Parse(text));
        Assert.DoesNotContain(loaded.Warnings, warning => warning.Message.Contains("not honored", StringComparison.Ordinal));

        var core = new Core { Plugins = fixture.Host };
        fixture.Host.Command = line => core.Execute(line).Text;
        core.Handle(new Configure(loaded));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
        Assert.Equal(new Rectangle(0, 0, 1920, 540), Bounds(core, 1));
        Assert.Equal(new Rectangle(0, 540, 1920, 540), Bounds(core, 2));
        Assert.Equal("[\"dwindle\",\"master\",\"scrolling\",\"monocle\",\"rows\"]", core.Execute("layouts").Text);

        var dropped = Flat + "general:layout = rows\n";
        var again = fixture.Host.Incorporate(ConfigParser.Parse(dropped), () => ConfigParser.Parse(dropped));
        Assert.Contains(again.Warnings, warning => warning.Message == "Layout 'rows' is not honored.");
        var reloaded = core.Handle(new Configure(again));
        Assert.Equal("dwindle", Workspace(reloaded));
    }

    [Fact]
    public void AWorkspaceRuleSelectsThePluginLayout()
    {
        using var fixture = Fixture.Create("rows");
        var text = Flat + "plugin = " + fixture.AssemblyPath + "\ngeneral:layout = dwindle\nworkspace = 2, layout:rows\n";
        var document = fixture.Host.Incorporate(ConfigParser.Parse(text), () => ConfigParser.Parse(text));
        Assert.DoesNotContain(document.Warnings, warning => warning.Message.Contains("rows", StringComparison.Ordinal));

        var core = new Core { Plugins = fixture.Host };
        fixture.Host.Command = line => core.Execute(line).Text;
        core.Handle(new Configure(document));
        core.Handle(new MonitorsChanged([new Monitor("DP-1", "One", 0, 0, 1920, 1080, 1, true, Area)]));
        core.Handle(new Dispatch("workspace", "2"));
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));

        Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
        Assert.Equal("rows", Workspace(core.Execute("j/activeworkspace").Text));
        Assert.Equal(new Rectangle(0, 0, 1920, 540), Bounds(core, 1));
        Assert.Equal(new Rectangle(0, 540, 1920, 540), Bounds(core, 2));
    }

    [Fact]
    public void ClosingAWindowRemovesItBeforeThePluginPlacesTheRest()
    {
        using var fixture = Fixture.Create("rows");
        var core = Open(fixture, "general:layout = rows\n");
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));
        Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
        Assert.Equal(new Rectangle(0, 0, 1920, 360), Bounds(core, 1));

        core.Handle(new WindowClosed(new WindowId(1)));
        Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
        Assert.Equal(new Rectangle(0, 0, 1920, 540), Bounds(core, 2));
        Assert.Equal(new Rectangle(0, 540, 1920, 540), Bounds(core, 3));
    }

    [Fact]
    public void ALayoutMessageCanSwitchToColumnsAndARejectionIsLogged()
    {
        using var fixture = Fixture.Create("rows");
        var core = Open(fixture, "general:layout = rows\n");
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));
        Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));

        Assert.Equal("ok", core.Execute("dispatch layoutmsg bad").Text);
        Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
        Assert.Contains(fixture.Log.Lines, line => line.Contains("rejected", StringComparison.Ordinal));
        Assert.Equal(new Rectangle(0, 0, 1920, 360), Bounds(core, 1));
        Assert.Equal(new Rectangle(0, 720, 1920, 360), Bounds(core, 3));

        Assert.Equal("ok", core.Execute("dispatch layoutmsg cols").Text);
        Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
        Assert.Equal(new Rectangle(0, 0, 640, 1080), Bounds(core, 1));
        Assert.Equal(new Rectangle(640, 0, 640, 1080), Bounds(core, 2));
        Assert.Equal(new Rectangle(1280, 0, 640, 1080), Bounds(core, 3));
    }

    [Fact]
    public void AFailingLayoutFallsBackToTheGridAndThenStopsBeingCalled()
    {
        using var fixture = Fixture.Create("faultylayout");
        Environment.SetEnvironmentVariable(FaultyLayoutPlugin.Calls, null);
        try
        {
            var core = Open(fixture, "general:layout = faulty\n");
            core.Handle(Appeared(1));
            Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
            core.Handle(Appeared(2));
            Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
            core.Handle(Appeared(3));
            Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
            Assert.Equal(new Rectangle(0, 0, 960, 540), Bounds(core, 1));
            Assert.Equal(new Rectangle(960, 0, 960, 540), Bounds(core, 2));
            Assert.Equal(new Rectangle(0, 540, 960, 540), Bounds(core, 3));
            Assert.Equal("3", Environment.GetEnvironmentVariable(FaultyLayoutPlugin.Calls));

            core.Handle(Appeared(4));
            Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
            Assert.Equal("3", Environment.GetEnvironmentVariable(FaultyLayoutPlugin.Calls));
            Assert.Equal(new Rectangle(0, 0, 960, 540), Bounds(core, 1));

            var listed = JsonDocument.Parse(core.Execute("plugin list").Text).RootElement;
            var plugin = Assert.Single(listed.EnumerateArray());
            Assert.Equal("disabled", plugin.GetProperty("state").GetString());
            Assert.Equal(3, plugin.GetProperty("failures").GetInt32());
            Assert.Equal("faulty", Workspace(core.Execute("j/activeworkspace").Text));
        }
        finally
        {
            Environment.SetEnvironmentVariable(FaultyLayoutPlugin.Calls, null);
        }
    }

    [Fact]
    public void AHungLayoutIsAbandonedAndTheGridIsUsed()
    {
        using var fixture = Fixture.Create(TimeSpan.FromMilliseconds(50), "hanglayout");
        var name = "winland-layout-" + Guid.NewGuid().ToString("N");
        using var release = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        Environment.SetEnvironmentVariable(HangingLayoutPlugin.Variable, name);
        var watch = Stopwatch.StartNew();
        try
        {
            var core = Open(fixture, "general:layout = hang\n");
            core.Handle(Appeared(1));
            core.Handle(Appeared(2));
            core.Handle(Appeared(3));
            Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
            watch.Stop();

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"abandoned layout took {watch.Elapsed}");
            Assert.Equal(new Rectangle(0, 0, 960, 540), Bounds(core, 1));
            Assert.Equal(new Rectangle(960, 0, 960, 540), Bounds(core, 2));
            Assert.Equal(new Rectangle(0, 540, 960, 540), Bounds(core, 3));
            Assert.Contains(fixture.Log.Lines, line => line.Contains("exceeded", StringComparison.Ordinal));
        }
        finally
        {
            watch.Stop();
            release.Set();
            Environment.SetEnvironmentVariable(HangingLayoutPlugin.Variable, null);
        }
    }

    [Fact]
    public void UnloadingAPluginLayoutReturnsWorkspacesToDwindle()
    {
        using var fixture = Fixture.Create("rows");
        var core = Open(fixture, "general:layout = rows\n");
        core.Handle(Appeared(1));
        core.Handle(Appeared(2));
        core.Handle(Appeared(3));
        Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
        Assert.Equal(new Rectangle(0, 0, 1920, 360), Bounds(core, 1));

        Assert.Equal("ok", core.Execute("plugin unload rows").Text);
        Assert.Equal("dwindle", Workspace(core.Execute("j/activeworkspace").Text));
        Assert.Equal(new Rectangle(0, 0, 960, 1080), Bounds(core, 1));
        Assert.Equal(new Rectangle(960, 0, 960, 540), Bounds(core, 2));
        Assert.Equal(new Rectangle(960, 540, 960, 540), Bounds(core, 3));
        Assert.Equal("[\"dwindle\",\"master\",\"scrolling\",\"monocle\"]", core.Execute("layouts").Text);
        Assert.Contains(ConfigParser.Parse("general:layout = rows\n").Warnings, warning => warning.Message == "Layout 'rows' is not honored.");

        Assert.Equal("ok", core.Execute("plugin load " + fixture.AssemblyPath).Text);
        Assert.True(fixture.Host.Settle(TimeSpan.FromSeconds(2)));
        Assert.Equal("rows", Workspace(core.Execute("j/activeworkspace").Text));
        Assert.Equal(new Rectangle(0, 0, 1920, 360), Bounds(core, 1));
        Assert.Equal(new Rectangle(0, 720, 1920, 360), Bounds(core, 3));
    }

    [Fact]
    public void AReservedOrDuplicateLayoutNameIsRefused()
    {
        using var reserved = Fixture.Create("reserved");
        var core = new Core { Plugins = reserved.Host };
        Assert.Equal("ok", core.Execute("plugin load " + reserved.AssemblyPath).Text);
        const string reasons = """
            Layout 'dwindle' is reserved.
            Layout 'Dwindle' is reserved.
            Layout 'default' is reserved.
            The layout name is empty.
            Layout names starting with lua: belong to a Lua layout.
            probe
            Layout 'probe' is already registered.
            """;
        Assert.Equal(reasons.ReplaceLineEndings("\n"), core.Execute("dispatch reasons").Text.ReplaceLineEndings("\n"));
        Assert.Equal("[\"dwindle\",\"master\",\"scrolling\",\"monocle\",\"probe\"]", core.Execute("layouts").Text);

        var approvals = Path.Combine(reserved.Directory, "approved.json");
        string hash;
        using (var document = JsonDocument.Parse(File.ReadAllText(approvals)))
            hash = document.RootElement[0].GetProperty("sha256").GetString()!;
        File.WriteAllText(approvals, JsonSerializer.Serialize(new[]
        {
            new { name = "reserved", sha256 = hash },
            new { name = "rows", sha256 = hash },
            new { name = "again", sha256 = hash },
        }));
        var reply = core.Execute("plugin load " + reserved.AssemblyPath);
        Assert.Contains("already registered", reply.Text, StringComparison.Ordinal);
        var names = JsonDocument.Parse(core.Execute("plugin list").Text).RootElement.EnumerateArray()
            .Select(plugin => plugin.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("rows", names);
        Assert.DoesNotContain("again", names);
        Assert.Equal(reasons.ReplaceLineEndings("\n"), core.Execute("dispatch reasons").Text.ReplaceLineEndings("\n"));
    }

    private static Core Open(Fixture fixture, string config)
    {
        var core = new Core { Plugins = fixture.Host };
        fixture.Host.Command = line => core.Execute(line).Text;
        Assert.Equal("ok", core.Execute("plugin load " + fixture.AssemblyPath).Text);
        var document = ConfigParser.Parse(Flat + config);
        Assert.DoesNotContain(document.Warnings, warning => warning.Message.Contains("not honored", StringComparison.Ordinal));
        core.Handle(new Configure(document));
        return core;
    }

    private static WindowAppeared Appeared(long window) => new(new WindowId(window), Area);

    private static string Workspace(Outcome outcome) => Workspace(outcome.State.Workspaces, outcome.State.ActiveWorkspace);

    private static string Workspace(string reply)
    {
        using var document = JsonDocument.Parse(reply);
        return document.RootElement.GetProperty("tiledLayout").GetString() ?? "";
    }

    private static string Workspace(IReadOnlyList<Workspace> workspaces, WorkspaceId active)
    {
        var workspace = Assert.Single(workspaces, candidate => candidate.Id == active);
        return workspace.TiledLayout;
    }

    private static Rectangle Bounds(Core core, long window)
    {
        using var document = JsonDocument.Parse(core.Execute("j/clients").Text);
        var address = "0x" + window.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var client in document.RootElement.EnumerateArray())
        {
            if (client.GetProperty("address").GetString() != address)
                continue;

            var at = client.GetProperty("at");
            var size = client.GetProperty("size");
            return new Rectangle(at[0].GetInt32(), at[1].GetInt32(), size[0].GetInt32(), size[1].GetInt32());
        }

        throw new Xunit.Sdk.XunitException("No client " + address);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string directory, string assemblyPath, PluginHost host, ListLog log)
        {
            Directory = directory;
            AssemblyPath = assemblyPath;
            Host = host;
            Log = log;
        }

        public string Directory { get; }

        public string AssemblyPath { get; }

        public PluginHost Host { get; }

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
            var host = new PluginHost(
                new AllowGuard(),
                log,
                new PluginHostOptions { Directory = directory, ApprovalsPath = approvalsPath, CallBudget = budget });
            return new Fixture(directory, assemblyPath, host, log);
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

        public string? DenyApprovals(FileStream approvals) => null;
    }

    private sealed class ListLog : IPluginLog
    {
        public List<string> Lines { get; } = [];

        public void Write(string message) => Lines.Add(message);
    }
}
