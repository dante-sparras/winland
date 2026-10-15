using Winland.Plugins;
using Winland.Plugins.Manager;

namespace Winland.Tests;

public class PluginApplyTests
{
    [Fact]
    public void InstallCopiesTheAssemblyAndApprovesTheHashOfTheCopy()
    {
        using var fixture = ManagerFixture.Create();
        var staged = Stage(fixture, "sample.dll");
        var hash = PluginHash.OfFile(staged);

        var result = Apply(fixture, Install(fixture, ("sample", hash, staged)));

        Assert.True(result.Ok, string.Join('\n', result.Lines));
        var target = Path.Combine(fixture.Paths.PluginDirectory, "sample.dll");
        Assert.Equal(hash, PluginHash.OfFile(target));
        var entry = Assert.Single(InstalledPlugins.Read(fixture.Paths.Approvals));
        Assert.Equal(new InstalledPlugin("sample", hash, target, ManagerFixture.Url, fixture.Commit), entry);
        Assert.Empty(Directory.GetFiles(fixture.Paths.PluginDirectory, "*.new"));
    }

    [Fact]
    public void NewPluginDirectoryIsProtected()
    {
        using var fixture = ManagerFixture.Create();
        var staged = Stage(fixture, "sample.dll");

        var result = Apply(fixture, Install(fixture, ("sample", PluginHash.OfFile(staged), staged)));

        Assert.True(result.Ok);
        Assert.Equal([Path.GetFullPath(fixture.Paths.PluginDirectory)], fixture.Protector.Protected);
        Assert.Contains(result.Lines, line => line.StartsWith("Created the plugin directory", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingPluginDirectoryIsNotProtectedAgain()
    {
        using var fixture = ManagerFixture.Create();
        Directory.CreateDirectory(fixture.Paths.PluginDirectory);
        var staged = Stage(fixture, "sample.dll");

        Assert.True(Apply(fixture, Install(fixture, ("sample", PluginHash.OfFile(staged), staged))).Ok);

        Assert.Empty(fixture.Protector.Protected);
    }

    [Fact]
    public void AssemblyChangedAfterItWasShownInstallsNothing()
    {
        using var fixture = ManagerFixture.Create();
        var first = Stage(fixture, "first.dll");
        var second = Stage(fixture, "second.dll");
        var shown = PluginHash.OfFile(second);
        File.AppendAllText(second, "tampered");

        var result = Apply(fixture, Install(fixture, ("first", PluginHash.OfFile(first), first), ("second", shown, second)));

        Assert.False(result.Ok);
        Assert.Contains("changed after it was shown", Assert.Single(result.Lines), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(fixture.Paths.PluginDirectory));
    }

    [Fact]
    public void ProtectionDenialRefusesTheChange()
    {
        using var fixture = ManagerFixture.Create();
        var staged = Stage(fixture, "sample.dll");
        var apply = new PluginApply(new ManagerFixture.DenyGuard("A non-administrator can write it."), fixture.Protector);

        var result = apply.Run(Install(fixture, ("sample", PluginHash.OfFile(staged), staged)));

        Assert.False(result.Ok);
        Assert.Equal(["A non-administrator can write it."], result.Lines);
        Assert.False(File.Exists(fixture.Paths.Approvals));
    }

    [Fact]
    public void PluginApprovedByHandIsNotReplaced()
    {
        using var fixture = ManagerFixture.Create();
        Directory.CreateDirectory(fixture.Paths.PluginDirectory);
        InstalledPlugins.Write(fixture.Paths.Approvals, [new InstalledPlugin("sample", new string('0', 64), null, null, null)]);
        var staged = Stage(fixture, "sample.dll");

        var result = Apply(fixture, Install(fixture, ("sample", PluginHash.OfFile(staged), staged)));

        Assert.False(result.Ok);
        Assert.Equal(["Plugin 'sample' is already approved by hand. Remove it first."], result.Lines);
    }

    [Fact]
    public void ReinstallingARepositoryReplacesItsPluginsAndKeepsOthers()
    {
        using var fixture = ManagerFixture.Create();
        var old = Stage(fixture, "old.dll");
        Assert.True(Apply(fixture, Install(fixture, ("sample", PluginHash.OfFile(old), old), ("gone", PluginHash.OfFile(old), old))).Ok);
        var manual = new InstalledPlugin("manual", new string('1', 64), null, null, null);
        InstalledPlugins.Write(fixture.Paths.Approvals, [.. InstalledPlugins.Read(fixture.Paths.Approvals), manual]);
        var fresh = Stage(fixture, "fresh.dll");
        File.AppendAllText(fresh, "v2");

        var result = Apply(fixture, Install(fixture, ("sample", PluginHash.OfFile(fresh), fresh)));

        Assert.True(result.Ok, string.Join('\n', result.Lines));
        var entries = InstalledPlugins.Read(fixture.Paths.Approvals);
        Assert.Equal(["manual", "sample"], entries.Select(entry => entry.Name).Order(StringComparer.Ordinal));
        Assert.Equal(PluginHash.OfFile(fresh), entries.Single(entry => entry.Name == "sample").Sha256);
        Assert.False(File.Exists(Path.Combine(fixture.Paths.PluginDirectory, "gone.dll")));
        Assert.Contains("Removed plugin 'gone'.", result.Lines);
    }

    [Fact]
    public void RemoveDeletesTheRepositorysFilesAndApprovals()
    {
        using var fixture = ManagerFixture.Create();
        var staged = Stage(fixture, "sample.dll");
        Assert.True(Apply(fixture, Install(fixture, ("sample", PluginHash.OfFile(staged), staged))).Ok);
        var request = new ApplyRequest(fixture.Paths.PluginDirectory, fixture.Paths.Approvals, null, [], [ManagerFixture.Url]);

        var result = Apply(fixture, request);

        Assert.True(result.Ok);
        Assert.Empty(InstalledPlugins.Read(fixture.Paths.Approvals));
        Assert.False(File.Exists(Path.Combine(fixture.Paths.PluginDirectory, "sample.dll")));
    }

    [Fact]
    public void RemovingARepositoryThatIsNotInstalledFails()
    {
        using var fixture = ManagerFixture.Create();
        var request = new ApplyRequest(fixture.Paths.PluginDirectory, fixture.Paths.Approvals, null, [], ["https://example.com/none"]);

        var result = Apply(fixture, request);

        Assert.False(result.Ok);
        Assert.Equal(["Repository 'https://example.com/none' is not installed."], result.Lines);
    }

    [Theory]
    [InlineData("../evil", "not a valid plugin name")]
    [InlineData("bad name", "not a valid plugin name")]
    public void InvalidPluginNameIsRefused(string name, string reason)
    {
        using var fixture = ManagerFixture.Create();
        var staged = Stage(fixture, "sample.dll");

        var result = Apply(fixture, Install(fixture, (name, PluginHash.OfFile(staged), staged)));

        Assert.False(result.Ok);
        Assert.Contains(reason, Assert.Single(result.Lines), StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.Paths.PluginDirectory));
    }

    [Fact]
    public void ShortHashIsRefused()
    {
        using var fixture = ManagerFixture.Create();
        var staged = Stage(fixture, "sample.dll");

        var result = Apply(fixture, Install(fixture, ("sample", "abc", staged)));

        Assert.False(result.Ok);
        Assert.Equal(["'abc' is not a SHA-256."], result.Lines);
    }

    [Fact]
    public void RequestSurvivesTheCommandLine()
    {
        var request = new ApplyRequest(
            @"C:\Program Data\plugins",
            @"C:\Program Data\plugins\approved.json",
            @"C:\Users\me\staging\apply.log",
            [
                new StagedRepository("https://a", "c1", [new StagedPlugin("one", new string('a', 64), @"C:\s\one.dll"), new StagedPlugin("two", new string('b', 64), @"C:\s\two.dll")]),
                new StagedRepository("https://b", "c2", [new StagedPlugin("three", new string('c', 64), @"C:\s\three.dll")]),
            ],
            ["https://c"]);

        var args = request.ToArguments();
        var parsed = ApplyRequest.Parse(args.Skip(2).ToArray(), out var error);

        Assert.Equal(["plugin", "apply"], args.Take(2));
        Assert.NotNull(parsed);
        Assert.Equal("", error);
        Assert.Equal(request.PluginDirectory, parsed.PluginDirectory);
        Assert.Equal(request.ApprovalsPath, parsed.ApprovalsPath);
        Assert.Equal(request.Report, parsed.Report);
        Assert.Equal(request.Remove, parsed.Remove);
        Assert.Equal(2, parsed.Install.Count);
        Assert.Equal(request.Install[0].Plugins, parsed.Install[0].Plugins);
        Assert.Equal(request.Install[1].Plugins, parsed.Install[1].Plugins);
        Assert.Equal("c2", parsed.Install[1].Commit);
    }

    [Theory]
    [InlineData("--plugin-dir", "d")]
    [InlineData("--plugin-dir", "d", "--approvals", "a")]
    [InlineData("--plugin-dir", "d", "--approvals", "a", "--plugin", "x", "y", "z")]
    [InlineData("--plugin-dir", "d", "--approvals", "a", "--bogus")]
    public void IncompleteRequestIsRefused(params string[] args)
    {
        Assert.Null(ApplyRequest.Parse(args, out var error));
        Assert.NotEqual("", error);
    }

    private static ApplyResult Apply(ManagerFixture fixture, ApplyRequest request) =>
        new PluginApply(new ManagerFixture.AllowGuard(), fixture.Protector).Run(request);

    private static ApplyRequest Install(ManagerFixture fixture, params (string Name, string Sha256, string Source)[] plugins) =>
        new(
            fixture.Paths.PluginDirectory,
            fixture.Paths.Approvals,
            null,
            [new StagedRepository(ManagerFixture.Url, fixture.Commit, plugins.Select(plugin => new StagedPlugin(plugin.Name, plugin.Sha256, plugin.Source)).ToArray())],
            []);

    private static string Stage(ManagerFixture fixture, string name)
    {
        var folder = Path.Combine(fixture.Paths.Staging, "built");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.Copy(ManagerFixture.SampleDll(), path, overwrite: true);
        return path;
    }
}
