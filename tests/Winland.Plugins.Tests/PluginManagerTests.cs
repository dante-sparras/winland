using Winland.Plugins;
using Winland.Plugins.Manager;

namespace Winland.Tests;

[Collection("Winland.Plugins")]
public class PluginManagerTests
{
    [Fact]
    public void InstallBuildsInStagingAndApprovesThroughOnePrompt()
    {
        using var fixture = ManagerFixture.Create();

        var code = fixture.Manager.Install(ManagerFixture.Url, null);

        Assert.True(code == 0, fixture.Error.ToString());
        Assert.Equal(1, fixture.Elevator.Prompts);
        var publish = Assert.Single(fixture.Runner.Commands, command => command.StartsWith("dotnet publish", StringComparison.Ordinal));
        Assert.Contains(fixture.Paths.Staging, publish, StringComparison.Ordinal);
        Assert.Contains($"-p:WinlandPluginApi={fixture.Paths.ApiAssembly}", publish, StringComparison.Ordinal);
        var target = Path.Combine(fixture.Paths.PluginDirectory, "sample.dll");
        var hash = PluginHash.OfFile(ManagerFixture.SampleDll());
        Assert.Equal(new InstalledPlugin("sample", hash, target, ManagerFixture.Url, fixture.Commit), Assert.Single(InstalledPlugins.Read(fixture.Paths.Approvals)));
        var shown = fixture.Output.ToString();
        Assert.Contains("Repository: " + ManagerFixture.Url, shown, StringComparison.Ordinal);
        Assert.Contains("Commit:     " + fixture.Commit, shown, StringComparison.Ordinal);
        Assert.Contains("SHA-256:  " + hash, shown, StringComparison.Ordinal);
        Assert.Contains("Installed plugin 'sample'", shown, StringComparison.Ordinal);
        Assert.Contains("plugin = " + target, shown, StringComparison.Ordinal);
    }

    // The staging folder belongs to the user, and a junction in it would redirect a write
    // the elevated step made there. The report comes back through the elevator instead.
    [Fact]
    public void TheElevatedStepIsNotToldToWriteIntoStaging()
    {
        using var fixture = ManagerFixture.Create();

        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));

        var arguments = Assert.Single(fixture.Elevator.Calls);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith(fixture.Paths.Staging, StringComparison.OrdinalIgnoreCase) && !argument.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("--report", arguments);
        Assert.Empty(Directory.GetFiles(fixture.Paths.Staging));
        Assert.Contains("Installed plugin 'sample'", fixture.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NothingLoadsFromStaging()
    {
        using var fixture = ManagerFixture.Create();
        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));
        var staged = Path.Combine(new PluginStaging(fixture.Paths, fixture.Runner).FolderOf(ManagerFixture.Url), "out", "sample", "SamplePlugin.dll");
        using var host = new PluginHost(
            new ManagerFixture.AllowGuard(),
            new ListLog(),
            new PluginHostOptions { Directory = fixture.Paths.PluginDirectory, ApprovalsPath = fixture.Paths.Approvals });

        var fromStaging = host.Load(staged);
        var installed = host.Load(Path.Combine(fixture.Paths.PluginDirectory, "sample.dll"));

        Assert.StartsWith("error:", fromStaging, StringComparison.Ordinal);
        Assert.Contains("outside the plugin directory", fromStaging, StringComparison.Ordinal);
        Assert.Equal("ok", installed);
        host.Unload("sample");
    }

    [Fact]
    public void ElevatedShellIsRefusedBeforeCloning()
    {
        using var fixture = ManagerFixture.Create();
        fixture.Elevator.IsElevated = true;

        Assert.Equal(1, fixture.Manager.Install(ManagerFixture.Url, null));
        Assert.Equal(1, fixture.Manager.Update(force: false));

        Assert.Empty(fixture.Runner.Commands);
        Assert.Equal(0, fixture.Elevator.Prompts);
        Assert.Contains("not elevated", fixture.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void IncompatibleApiVersionIsRefusedBeforeBuilding()
    {
        using var fixture = ManagerFixture.Create();
        fixture.ManifestText = ManagerFixture.Manifest + "[future]\napi_version = 2\nproject = \"Sample/Sample.csproj\"\n";

        var code = fixture.Manager.Install(ManagerFixture.Url, null);

        Assert.Equal(1, code);
        Assert.DoesNotContain(fixture.Runner.Commands, command => command.StartsWith("dotnet", StringComparison.Ordinal));
        Assert.Equal(0, fixture.Elevator.Prompts);
        Assert.Contains("Plugin 'future' declares API version 2; Winland accepts 1.", fixture.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was built.", fixture.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidManifestIsRefusedBeforeBuilding()
    {
        using var fixture = ManagerFixture.Create();
        fixture.ManifestText = "[p]\napi_version = 1\n";

        Assert.Equal(1, fixture.Manager.Install(ManagerFixture.Url, null));

        Assert.Contains("winland-plugin.toml is not valid", fixture.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.Elevator.Prompts);
    }

    [Fact]
    public void FailedBuildShowsTheOutputAndAsksNothing()
    {
        using var fixture = ManagerFixture.Create();
        fixture.Runner.FailBuild = true;

        Assert.Equal(1, fixture.Manager.Install(ManagerFixture.Url, null));

        Assert.Contains("Plugin 'sample' failed to build.", fixture.Error.ToString(), StringComparison.Ordinal);
        Assert.Contains("CS1002", fixture.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.Elevator.Prompts);
    }

    [Fact]
    public void CancelledPromptChangesNothing()
    {
        using var fixture = ManagerFixture.Create();
        fixture.Elevator.Cancel = true;

        Assert.Equal(1, fixture.Manager.Install(ManagerFixture.Url, null));

        Assert.False(File.Exists(fixture.Paths.Approvals));
        Assert.Contains("Nothing was changed.", fixture.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RevisionIsCheckedOut()
    {
        using var fixture = ManagerFixture.Create();

        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, "v1.2"));

        Assert.Contains(fixture.Runner.Commands, command => command.Contains("reset --hard --recurse-submodules v1.2", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--upload-pack=evil", null)]
    [InlineData(ManagerFixture.Url, "--orphan")]
    public void OptionLikeArgumentIsRefused(string url, string? revision)
    {
        using var fixture = ManagerFixture.Create();

        Assert.Equal(1, fixture.Manager.Install(url, revision));

        Assert.Empty(fixture.Runner.Commands);
    }

    [Fact]
    public void InstalledRepositoryIsNotInstalledTwice()
    {
        using var fixture = ManagerFixture.Create();
        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));
        fixture.Runner.Commands.Clear();

        Assert.Equal(1, fixture.Manager.Install(ManagerFixture.Url, null));

        Assert.Empty(fixture.Runner.Commands);
        Assert.Contains("already installed", fixture.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateSkipsARepositoryAtTheSameCommit()
    {
        using var fixture = ManagerFixture.Create();
        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));
        fixture.Runner.Commands.Clear();

        Assert.Equal(0, fixture.Manager.Update(force: false));

        Assert.DoesNotContain(fixture.Runner.Commands, command => command.StartsWith("dotnet", StringComparison.Ordinal));
        Assert.Equal(1, fixture.Elevator.Prompts);
        Assert.Contains("is up to date", fixture.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateRebuildsANewCommitAndReloadsALoadedPlugin()
    {
        using var fixture = ManagerFixture.Create();
        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));
        fixture.Running.Loaded.Add("sample");
        fixture.Commit = new string('b', 40);

        Assert.Equal(0, fixture.Manager.Update(force: false));

        Assert.Equal(2, fixture.Elevator.Prompts);
        Assert.Equal(new string('b', 40), Assert.Single(InstalledPlugins.Read(fixture.Paths.Approvals)).Commit);
        Assert.Equal(["unload sample", "load " + Path.Combine(fixture.Paths.PluginDirectory, "sample.dll")], fixture.Running.Calls);
        Assert.Contains("Reloaded plugin 'sample'.", fixture.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateUnloadsAPluginTheManifestNoLongerLists()
    {
        using var fixture = ManagerFixture.Create();
        fixture.ManifestText = ManagerFixture.Manifest + "[extra]\napi_version = 1\nproject = \"Sample/Sample.csproj\"\nassembly = \"SamplePlugin.dll\"\n";
        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));
        fixture.Running.Loaded.Add("extra");
        fixture.ManifestText = ManagerFixture.Manifest;
        fixture.Commit = new string('b', 40);

        Assert.Equal(0, fixture.Manager.Update(force: false));

        Assert.Equal("sample", Assert.Single(InstalledPlugins.Read(fixture.Paths.Approvals)).Name);
        Assert.Contains("unload extra", fixture.Running.Calls);
        Assert.DoesNotContain("extra", fixture.Running.Loaded);
        Assert.Contains("Unloaded plugin 'extra'", fixture.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateReportsAPluginThatDidNotLoadAgain()
    {
        using var fixture = ManagerFixture.Create();
        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));
        fixture.Running.Loaded.Add("sample");
        fixture.Running.FailLoad = true;
        fixture.Commit = new string('b', 40);

        Assert.Equal(1, fixture.Manager.Update(force: false));

        Assert.Contains("Plugin 'sample' was unloaded but did not load again", fixture.Error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Reloaded plugin 'sample'", fixture.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ForcedUpdateRebuildsTheSameCommit()
    {
        using var fixture = ManagerFixture.Create();
        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));
        fixture.Runner.Commands.Clear();

        Assert.Equal(0, fixture.Manager.Update(force: true));

        Assert.Contains(fixture.Runner.Commands, command => command.StartsWith("dotnet publish", StringComparison.Ordinal));
        Assert.Equal(2, fixture.Elevator.Prompts);
    }

    [Fact]
    public void RemoveByNameDeletesTheRepositoryAndUnloadsIt()
    {
        using var fixture = ManagerFixture.Create();
        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));
        fixture.Running.Loaded.Add("sample");

        Assert.Equal(0, fixture.Manager.Remove("sample"));

        Assert.Empty(InstalledPlugins.Read(fixture.Paths.Approvals));
        Assert.False(File.Exists(Path.Combine(fixture.Paths.PluginDirectory, "sample.dll")));
        Assert.False(Directory.Exists(new PluginStaging(fixture.Paths, fixture.Runner).FolderOf(ManagerFixture.Url)));
        Assert.Equal(["unload sample"], fixture.Running.Calls);
    }

    [Fact]
    public void RemoveByUrlWorks()
    {
        using var fixture = ManagerFixture.Create();
        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));

        Assert.Equal(0, fixture.Manager.Remove(ManagerFixture.Url));

        Assert.Empty(InstalledPlugins.Read(fixture.Paths.Approvals));
    }

    [Fact]
    public void RemoveOfSomethingUnknownFails()
    {
        using var fixture = ManagerFixture.Create();

        Assert.Equal(1, fixture.Manager.Remove("nothing"));

        Assert.Contains("No installed repository or plugin is named 'nothing'.", fixture.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.Elevator.Prompts);
    }

    [Fact]
    public void ListShowsRepositoriesAndHandApprovals()
    {
        using var fixture = ManagerFixture.Create();
        Assert.Equal(0, fixture.Manager.Install(ManagerFixture.Url, null));
        InstalledPlugins.Write(
            fixture.Paths.Approvals,
            [.. InstalledPlugins.Read(fixture.Paths.Approvals), new InstalledPlugin("manual", new string('1', 64), null, null, null)]);
        fixture.Output.GetStringBuilder().Clear();

        Assert.Equal(0, fixture.Manager.List());

        var lines = fixture.Output.ToString().ReplaceLineEndings("\n").TrimEnd().Split('\n');
        Assert.Equal($"Repository {ManagerFixture.Url} (commit {fixture.Commit})", lines[0]);
        Assert.StartsWith("  sample  ", lines[1], StringComparison.Ordinal);
        Assert.Equal("Approved by hand", lines[2]);
        Assert.StartsWith("  manual  -  sha256 1111", lines[3], StringComparison.Ordinal);
    }

    [Fact]
    public void ListWithNothingInstalledSaysSo()
    {
        using var fixture = ManagerFixture.Create();

        Assert.Equal(0, fixture.Manager.List());

        Assert.Equal("No plugins are installed.", fixture.Output.ToString().Trim());
    }

    [Theory]
    [InlineData("https://github.com/someone/my-plugins.git", "my-plugins-")]
    [InlineData("https://github.com/someone/my-plugins/", "my-plugins-")]
    [InlineData(@"C:\repos\local", "local-")]
    [InlineData("git@host:some/thing.git", "thing-")]
    public void RepositoryFolderIsNamedAfterTheUrl(string url, string prefix)
    {
        var id = PluginStaging.RepositoryId(url);

        Assert.StartsWith(prefix, id, StringComparison.Ordinal);
        Assert.Equal(prefix.Length + 8, id.Length);
        Assert.NotEqual(id, PluginStaging.RepositoryId(url + "x"));
    }

    [Theory]
    [InlineData(new[] { "plugin", "install", "https://x" }, "Install")]
    [InlineData(new[] { "plugin", "install", "https://x", "abc" }, "Install")]
    [InlineData(new[] { "plugin", "update" }, "Update")]
    [InlineData(new[] { "plugin", "update", "--force" }, "Update")]
    [InlineData(new[] { "plugin", "remove", "sample" }, "Remove")]
    [InlineData(new[] { "plugin", "list", "--installed" }, "List")]
    [InlineData(new[] { "plugin", "install" }, "Invalid")]
    [InlineData(new[] { "plugin", "update", "now" }, "Invalid")]
    [InlineData(new[] { "plugin", "apply" }, "Invalid")]
    public void ManagerCommandsAreRecognized(string[] args, string kind)
    {
        var command = ManagerInvocation.Parse(args);

        Assert.NotNull(command);
        Assert.Equal(kind, command.GetType().Name);
    }

    [Theory]
    [InlineData("plugin list")]
    [InlineData("plugin load x.dll")]
    [InlineData("plugin unload x")]
    [InlineData("clients")]
    public void PipeCommandsAreLeftToTheControlInvocation(string line)
    {
        Assert.Null(ManagerInvocation.Parse(line.Split(' ')));
    }

    [Fact]
    public void ForceFlagIsRead()
    {
        Assert.Equal(new ManagerCommand.Update(true), ManagerInvocation.Parse(["plugin", "update", "-f"]));
        Assert.Equal(new ManagerCommand.Install("u", "r"), ManagerInvocation.Parse(["plugin", "install", "u", "r"]));
    }

    private sealed class ListLog : IPluginLog
    {
        public void Write(string message)
        {
        }
    }
}
