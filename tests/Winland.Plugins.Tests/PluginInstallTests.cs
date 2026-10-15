using Winland.Plugins;
using Winland.Plugins.Manager;

namespace Winland.Tests;

/// <summary>
/// Installs the sample repository for real: git clones it, dotnet publishes it in staging, and the
/// elevated step runs in-process against a temporary plugin directory instead of behind UAC.
/// </summary>
[Collection("Winland.Plugins")]
public class PluginInstallTests
{
    [Fact]
    public void SampleRepositoryInstallsBuildsAndLoads()
    {
        using var fixture = ManagerFixture.Create();
        var repository = Path.Combine(fixture.Root, "repository");
        CopyTree(Path.Combine(AppContext.BaseDirectory, "SampleRepository"), repository);
        var runner = new SystemProcessRunner();
        Git(runner, repository, "init", "--quiet");
        Git(runner, repository, "add", ".");
        Git(runner, repository, "-c", "user.name=winland", "-c", "user.email=winland@example.com", "commit", "--quiet", "-m", "sample");
        var commit = runner.Run(PluginStaging.Git, ["rev-parse", "HEAD"], repository).Output.Trim();
        var paths = fixture.Paths with { ApiAssembly = Path.Combine(AppContext.BaseDirectory, "Winland.PluginApi.dll") };
        var manager = new PluginManager(paths, runner, fixture.Elevator, fixture.Running, fixture.Output, fixture.Error);

        var code = manager.Install(repository, null);

        Assert.True(code == 0, fixture.Output + "\n" + fixture.Error);
        var installed = Assert.Single(InstalledPlugins.Read(paths.Approvals));
        Assert.Equal("echo", installed.Name);
        Assert.Equal(commit, installed.Commit);
        Assert.Equal(PluginHash.OfFile(installed.File!), installed.Sha256);
        var staged = Path.Combine(new PluginStaging(paths, runner).FolderOf(repository), "out", "echo", "Echo.dll");
        Assert.Equal(installed.Sha256, PluginHash.OfFile(staged));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(staged)!, "Winland.PluginApi.dll")));

        using var host = new PluginHost(
            new ManagerFixture.AllowGuard(),
            new NoLog(),
            new PluginHostOptions { Directory = paths.PluginDirectory, ApprovalsPath = paths.Approvals });
        var core = new Core { Plugins = host };
        Assert.StartsWith("error:", core.Execute("plugin load " + staged).Text, StringComparison.Ordinal);
        Assert.Equal("ok", core.Execute("plugin load " + installed.File).Text);
        Assert.Equal("hello", core.Execute("dispatch echo hello").Text);
        Assert.Equal("ok", core.Execute("plugin unload echo").Text);
    }

    private static void Git(IProcessRunner runner, string repository, params string[] args)
    {
        var result = runner.Run(PluginStaging.Git, args, repository);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private sealed class NoLog : IPluginLog
    {
        public void Write(string message)
        {
        }
    }
}
