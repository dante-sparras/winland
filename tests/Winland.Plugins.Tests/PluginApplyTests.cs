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
        Assert.Equal([fixture.Root, Path.GetFullPath(fixture.Paths.PluginDirectory)], fixture.Protector.Protected);
        Assert.Contains(result.Lines, line => line.StartsWith("Created the plugin directory", StringComparison.Ordinal));
    }

    // A non-administrator may have created the directory, or ProgramData\Winland above it, first.
    // Finding them proves nothing. The parent goes first: its owner could swap the directory.
    [Fact]
    public void ExistingPluginDirectoryAndItsParentAreProtectedAgain()
    {
        using var fixture = ManagerFixture.Create();
        Directory.CreateDirectory(fixture.Paths.PluginDirectory);
        var staged = Stage(fixture, "sample.dll");

        Assert.True(Apply(fixture, Install(fixture, ("sample", PluginHash.OfFile(staged), staged))).Ok);

        Assert.Equal([fixture.Root, Path.GetFullPath(fixture.Paths.PluginDirectory)], fixture.Protector.Protected);
    }

    // The kept approvals are read through a handle the guard checked, not by path after the check.
    [Fact]
    public void ApprovalsTheGuardRefusesOnTheOpenFileInstallNothing()
    {
        using var fixture = ManagerFixture.Create();
        Directory.CreateDirectory(fixture.Paths.PluginDirectory);
        InstalledPlugins.Write(fixture.Paths.Approvals, [new InstalledPlugin("planted", new string('1', 64), null, null, null)]);
        var staged = Stage(fixture, "sample.dll");
        var apply = new PluginApply(new ApprovalsGuard("A non-administrator can write the open approvals."), fixture.Protector, fixture.Paths.PluginDirectory);

        var result = apply.Run(Install(fixture, ("sample", PluginHash.OfFile(staged), staged)));

        Assert.False(result.Ok);
        Assert.Equal(["A non-administrator can write the open approvals."], result.Lines);
        Assert.False(File.Exists(Path.Combine(fixture.Paths.PluginDirectory, "sample.dll")));
        Assert.Equal("planted", Assert.Single(InstalledPlugins.Read(fixture.Paths.Approvals)).Name);
    }

    [Fact]
    public void PluginDirectoryThatIsAJunctionIsRefused()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var fixture = ManagerFixture.Create();
        var elsewhere = Path.Combine(fixture.Root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Junctions.Create(fixture.Paths.PluginDirectory, elsewhere);
        var staged = Stage(fixture, "sample.dll");

        var result = Apply(fixture, Install(fixture, ("sample", PluginHash.OfFile(staged), staged)));

        Assert.False(result.Ok);
        Assert.Contains("reparse point", Assert.Single(result.Lines), StringComparison.Ordinal);
        Assert.Empty(fixture.Protector.Protected);
        Assert.Empty(Directory.GetFiles(elsewhere));
    }

    // The directory and the approvals path come from a non-elevated program, which can set
    // WINLAND_PLUGIN_DIR to any folder. Only the default one is created or protected.
    [Fact]
    public void AnotherPluginDirectoryIsNeitherCreatedNorProtected()
    {
        using var fixture = ManagerFixture.Create();
        var other = Path.Combine(fixture.Root, "other");
        var staged = Stage(fixture, "sample.dll");

        var missing = Apply(fixture, Install(fixture, other, ("sample", PluginHash.OfFile(staged), staged)));
        Directory.CreateDirectory(other);
        var unprepared = Apply(fixture, Install(fixture, other, ("sample", PluginHash.OfFile(staged), staged)));

        Assert.False(missing.Ok);
        Assert.Contains("not the default plugin directory", Assert.Single(missing.Lines), StringComparison.Ordinal);
        Assert.False(unprepared.Ok);
        Assert.Contains("not the default plugin directory", Assert.Single(unprepared.Lines), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(other));
        Assert.Empty(fixture.Protector.Protected);
    }

    [Fact]
    public void AnotherPluginDirectoryThatAnAdministratorPreparedIsUsed()
    {
        using var fixture = ManagerFixture.Create();
        var other = Path.Combine(fixture.Root, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "approved.json"), "[]");
        var staged = Stage(fixture, "sample.dll");

        var result = Apply(fixture, Install(fixture, other, ("sample", PluginHash.OfFile(staged), staged)));

        Assert.True(result.Ok, string.Join('\n', result.Lines));
        Assert.True(File.Exists(Path.Combine(other, "sample.dll")));
        Assert.Empty(fixture.Protector.Protected);
    }

    [Fact]
    public void AnotherPluginDirectoryMustPassTheGuard()
    {
        using var fixture = ManagerFixture.Create();
        var other = Path.Combine(fixture.Root, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "approved.json"), "[]");
        var staged = Stage(fixture, "sample.dll");
        var apply = new PluginApply(new ManagerFixture.DenyGuard("A non-administrator can write it."), fixture.Protector, fixture.Paths.PluginDirectory);

        var result = apply.Run(Install(fixture, other, ("sample", PluginHash.OfFile(staged), staged)));

        Assert.False(result.Ok);
        Assert.Equal(["A non-administrator can write it."], result.Lines);
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

    // A file the host or a scanner holds open cannot be replaced. Nothing may be left half done:
    // new assemblies under old approvals would not load, and old ones would lose theirs.
    [Fact]
    public void AFileThatCannotBeReplacedLeavesEverythingAsItWas()
    {
        using var fixture = ManagerFixture.Create();
        var old = Stage(fixture, "old.dll");
        Assert.True(Apply(fixture, Install(fixture, ("first", PluginHash.OfFile(old), old), ("second", PluginHash.OfFile(old), old))).Ok);
        var approvalsBefore = File.ReadAllText(fixture.Paths.Approvals);
        var fresh = Stage(fixture, "fresh.dll");
        File.AppendAllText(fresh, "v2");
        var first = Path.Combine(fixture.Paths.PluginDirectory, "first.dll");
        var second = Path.Combine(fixture.Paths.PluginDirectory, "second.dll");

        ApplyResult result;
        using (new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None))
            result = Apply(fixture, Install(fixture, ("first", PluginHash.OfFile(fresh), fresh), ("second", PluginHash.OfFile(fresh), fresh)));

        Assert.False(result.Ok);
        Assert.Contains("Nothing was changed", Assert.Single(result.Lines), StringComparison.Ordinal);
        Assert.Equal(approvalsBefore, File.ReadAllText(fixture.Paths.Approvals));
        Assert.Equal(PluginHash.OfFile(old), PluginHash.OfFile(first));
        Assert.Equal(PluginHash.OfFile(old), PluginHash.OfFile(second));
        Assert.Equal(
            ["approved.json", "first.dll", "second.dll"],
            Directory.GetFiles(fixture.Paths.PluginDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AFailedNewFileIsRemovedAgain()
    {
        using var fixture = ManagerFixture.Create();
        var old = Stage(fixture, "old.dll");
        Assert.True(Apply(fixture, Install(fixture, ("second", PluginHash.OfFile(old), old))).Ok);
        var approvalsBefore = File.ReadAllText(fixture.Paths.Approvals);
        var second = Path.Combine(fixture.Paths.PluginDirectory, "second.dll");

        ApplyResult result;
        using (new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None))
            result = Apply(fixture, Install(fixture, ("brandnew", PluginHash.OfFile(old), old), ("second", PluginHash.OfFile(old), old)));

        Assert.False(result.Ok);
        Assert.Equal(approvalsBefore, File.ReadAllText(fixture.Paths.Approvals));
        Assert.False(File.Exists(Path.Combine(fixture.Paths.PluginDirectory, "brandnew.dll")));
        Assert.Equal(
            ["approved.json", "second.dll"],
            Directory.GetFiles(fixture.Paths.PluginDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ProtectionDenialRefusesTheChange()
    {
        using var fixture = ManagerFixture.Create();
        var staged = Stage(fixture, "sample.dll");
        var apply = new PluginApply(new ManagerFixture.DenyGuard("A non-administrator can write it."), fixture.Protector, fixture.Paths.PluginDirectory);

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

    // Each plugin is installed as <name>.dll, and Windows file names do not differ by case.
    [Fact]
    public void ANameThatDiffersOnlyByCaseIsTheSamePlugin()
    {
        using var fixture = ManagerFixture.Create();
        Directory.CreateDirectory(fixture.Paths.PluginDirectory);
        InstalledPlugins.Write(fixture.Paths.Approvals, [new InstalledPlugin("sample", new string('0', 64), null, null, null)]);
        var staged = Stage(fixture, "sample.dll");
        var hash = PluginHash.OfFile(staged);

        var taken = Apply(fixture, Install(fixture, ("Sample", hash, staged)));
        var twice = Apply(fixture, Install(fixture, ("other", hash, staged), ("Other", hash, staged)));

        Assert.False(taken.Ok);
        Assert.Equal(["Plugin 'Sample' is already approved by hand. Remove it first."], taken.Lines);
        Assert.False(twice.Ok);
        Assert.Equal(["Plugin 'Other' is listed twice."], twice.Lines);
    }

    // The approvals are written before the old files are deleted, so a file that is held open is reported.
    [Fact]
    public void ARemovedPluginWhoseFileIsHeldOpenIsReported()
    {
        using var fixture = ManagerFixture.Create();
        var old = Stage(fixture, "old.dll");
        Assert.True(Apply(fixture, Install(fixture, ("sample", PluginHash.OfFile(old), old), ("gone", PluginHash.OfFile(old), old))).Ok);
        var gone = Path.Combine(fixture.Paths.PluginDirectory, "gone.dll");

        ApplyResult result;
        using (new FileStream(gone, FileMode.Open, FileAccess.Read, FileShare.Read))
            result = Apply(fixture, Install(fixture, ("sample", PluginHash.OfFile(old), old)));

        Assert.True(result.Ok, string.Join('\n', result.Lines));
        Assert.Contains("Removed plugin 'gone'.", result.Lines);
        Assert.Contains(result.Lines, line => line.StartsWith($"Could not delete '{gone}'", StringComparison.Ordinal));
        Assert.Equal(["sample"], InstalledPlugins.Read(fixture.Paths.Approvals).Select(entry => entry.Name));
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
            "winland-apply-" + new string('0', 32),
            [
                new StagedRepository("https://a", "c1", [new StagedPlugin("one", new string('a', 64), @"C:\s\one.dll"), new StagedPlugin("two", new string('b', 64), @"C:\s\two.dll")]),
                new StagedRepository("https://b", "c2", [new StagedPlugin("three", new string('c', 64), @"C:\s\three.dll")]),
            ],
            ["https://c"]) { ReportOwner = "S-1-5-21-1-2-3-1001" };

        var args = request.ToArguments();
        var parsed = ApplyRequest.Parse(args.Skip(2).ToArray(), out var error);

        Assert.Equal(["plugin", "apply"], args.Take(2));
        Assert.NotNull(parsed);
        Assert.Equal("", error);
        Assert.Equal(request.PluginDirectory, parsed.PluginDirectory);
        Assert.Equal(request.ApprovalsPath, parsed.ApprovalsPath);
        Assert.Equal(request.ReportPipe, parsed.ReportPipe);
        Assert.Equal("S-1-5-21-1-2-3-1001", parsed.ReportOwner);
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
    [InlineData("--plugin-dir", "d", "--approvals", "a", "--remove", "u", "--report", @"C:\Users\me\apply.log")]
    [InlineData("--plugin-dir", "d", "--approvals", "a", "--remove", "u", "--report-pipe", @"C:\Users\me\apply.log")]
    [InlineData("--plugin-dir", "d", "--approvals", "a", "--remove", "u", "--report-pipe", @"\\server\pipe\winland-apply-00000000000000000000000000000000")]
    [InlineData("--plugin-dir", "d", "--approvals", "a", "--remove", "u", "--report-pipe", "winland-apply-xyz")]
    [InlineData("--plugin-dir", "d", "--approvals", "a", "--remove", "u", "--report-owner", "Everyone")]
    [InlineData("--plugin-dir", "d", "--approvals", "a", "--remove", "u", "--report-owner", "S-1-")]
    public void IncompleteRequestIsRefused(params string[] args)
    {
        Assert.Null(ApplyRequest.Parse(args, out var error));
        Assert.NotEqual("", error);
    }

    private static ApplyResult Apply(ManagerFixture fixture, ApplyRequest request) =>
        new PluginApply(new ManagerFixture.AllowGuard(), fixture.Protector, fixture.Paths.PluginDirectory).Run(request);

    private static ApplyRequest Install(ManagerFixture fixture, params (string Name, string Sha256, string Source)[] plugins) =>
        new(
            fixture.Paths.PluginDirectory,
            fixture.Paths.Approvals,
            null,
            [new StagedRepository(ManagerFixture.Url, fixture.Commit, plugins.Select(plugin => new StagedPlugin(plugin.Name, plugin.Sha256, plugin.Source)).ToArray())],
            []);

    private static ApplyRequest Install(ManagerFixture fixture, string directory, params (string Name, string Sha256, string Source)[] plugins) =>
        Install(fixture, plugins) with { PluginDirectory = directory, ApprovalsPath = Path.Combine(directory, "approved.json") };

    private sealed class ApprovalsGuard(string message) : IPluginLocationGuard
    {
        public string? Deny(string pluginDirectory, string approvalsFile) => null;

        public string? DenyApprovals(FileStream approvals) => message;
    }

    private static string Stage(ManagerFixture fixture, string name)
    {
        var folder = Path.Combine(fixture.Paths.Staging, "built");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.Copy(ManagerFixture.SampleDll(), path, overwrite: true);
        return path;
    }
}
