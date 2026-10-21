using System.Security.Cryptography;
using Winland.Install;

namespace Winland.Install.Tests;

public class ReleaseUpdateTests
{
    private const string Source = @"C:\Users\me\Downloads\Winland\winland\winland.exe";

    private const string Machine = @"C:\Program Files\Winland";

    private const string User = @"C:\Users\me\AppData\Local\Programs\Winland";

    private const string Config = @"C:\Users\me\AppData\Roaming\winland";

    private const string ArchiveUrl = "https://github.com/dante-sparras/winland/releases/download/v0.2.0/Winland-win-x64.zip";

    private const string SumsUrl = "https://github.com/dante-sparras/winland/releases/download/v0.2.0/SHA256SUMS";

    private static readonly byte[] Archive = "published-zip"u8.ToArray();

    private readonly MemoryFileTree files = new();
    private readonly MemoryTaskCatalog tasks = new();
    private readonly MemoryRegistry registry = new();
    private readonly MemorySession session = new();
    private readonly AppInstall install;

    public ReleaseUpdateTests()
    {
        install = new AppInstall(
            new InstallLayout(Machine, User, @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs", @"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs", Config, @"C:\Users\me\AppData\Local\Winland"),
            tasks,
            files,
            registry,
            new MemoryStartMenu(),
            session);
        files.Seed(Source, [1]);
        files.Seed(@"C:\Users\me\Downloads\Winland\winland\winland.dll", [2]);
        files.Seed(@"C:\Users\me\Downloads\Winland\winlandctl\winlandctl.exe", [3]);
        files.Seed(Path.Combine(Machine, "winland.exe"), [8]);
        files.Seed(Path.Combine(Machine, "winlandctl.exe"), [6]);
        files.Seed(Path.Combine(User, "winland.exe"), [7]);
        files.Seed(Path.Combine(Config, "winland.conf"), [9]);
        tasks.Seed(LogonTaskDefinition.Build(Path.Combine(Machine, "winland.exe"), @"DESKTOP\dante"));
    }

    [Fact]
    public void AFolderThatIsNotAnInstallStopsBeforeTheFeed()
    {
        var feed = new ScriptedFeed(Archive, Sums(Archive), "0.2.0");

        var result = Update(feed).Run("0.1.0", @"C:\Dev\winland\winland.exe", Machine, User, elevated: true);

        Assert.IsType<UpdateResult.NotInstalled>(result);
        Assert.Equal(0, feed.LatestCalls);
        Assert.Empty(session.Operations);
        Assert.Equal([8], files.Read(Path.Combine(Machine, "winland.exe")));
    }

    [Fact]
    public void TheSameVersionStopsBeforeTheZip()
    {
        var feed = new ScriptedFeed(Archive, Sums(Archive), "0.1.0");

        var result = Update(feed).Run("0.1.0", MachineExe(), Machine, User, elevated: true);

        Assert.IsType<UpdateResult.Current>(result);
        Assert.Equal(1, feed.LatestCalls);
        Assert.Empty(feed.Downloads);
        Assert.Empty(session.Operations);
        Assert.Equal([8], files.Read(Path.Combine(Machine, "winland.exe")));
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
    }

    [Fact]
    public void AMachineInstallAsksForAnAdministratorAfterTheHashMatches()
    {
        var feed = new ScriptedFeed(Archive, Sums(Archive), "0.2.0");
        var package = new FixedPackage(Source);

        var result = new ReleaseUpdate(feed, package, install, () => throw new InvalidOperationException("staged"), _ => throw new InvalidOperationException("removed"))
            .Run("0.1.0", MachineExe(), Machine, User, elevated: false);

        Assert.IsType<UpdateResult.NeedsAdministrator>(result);
        Assert.Equal([ArchiveUrl, SumsUrl], feed.Downloads);
        Assert.Equal(0, package.Calls);
        Assert.Empty(session.Operations);
        Assert.Equal([8], files.Read(Path.Combine(Machine, "winland.exe")));
    }

    [Fact]
    public void AMismatchedHashChangesNothing()
    {
        var feed = new ScriptedFeed(Archive, Sums(new byte[] { 9 }), "0.2.0");
        var package = new FixedPackage(Source);
        var staged = 0;

        var result = new ReleaseUpdate(feed, package, install, () => { staged++; return @"C:\stage"; }, _ => { })
            .Run("0.1.0", MachineExe(), Machine, User, elevated: false);

        Assert.IsType<UpdateResult.Rejected>(result);
        Assert.Equal([ArchiveUrl, SumsUrl], feed.Downloads);
        Assert.Equal(0, package.Calls);
        Assert.Equal(0, staged);
        Assert.Empty(session.Operations);
        Assert.Equal([8], files.Read(Path.Combine(Machine, "winland.exe")));
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
        Assert.Empty(tasks.Operations);
    }

    [Fact]
    public void ANewerMachineReleaseReplacesTheInstallItWasRunFrom()
    {
        var feed = new ScriptedFeed(Archive, Sums(Archive), "0.2.0");
        string? removed = null;

        var result = Update(feed, path => removed = path).Run("0.1.0", MachineExe(), Machine, User, elevated: true);

        var updated = Assert.IsType<UpdateResult.Updated>(result);
        Assert.Equal("0.2.0", updated.Version);
        Assert.Equal([1], files.Read(Path.Combine(Machine, "winland.exe")));
        Assert.Equal([3], files.Read(Path.Combine(Machine, "winlandctl.exe")));
        Assert.Equal([7], files.Read(Path.Combine(User, "winland.exe")));
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
        Assert.Equal(["stop", "start"], session.Operations);
        Assert.Empty(session.StartedArguments);
        Assert.Equal("0.2.0", registry.GetString(RegistryRoot.Machine, InstallLocations.UninstallKey, "DisplayVersion"));
        Assert.Empty(tasks.Operations);
        Assert.Equal(@"C:\stage", removed);
    }

    [Fact]
    public void AUserInstallUpdatesWithoutAnAdministrator()
    {
        var feed = new ScriptedFeed(Archive, Sums(Archive), "0.2.0");

        var result = Update(feed).Run("0.0.0-dev", UserExe(), Machine, User, elevated: false);

        Assert.IsType<UpdateResult.Updated>(result);
        Assert.Equal([1], files.Read(Path.Combine(User, "winland.exe")));
        Assert.Equal([8], files.Read(Path.Combine(Machine, "winland.exe")));
        Assert.Equal(["--no-elevate"], session.StartedArguments);
        Assert.Equal("0.2.0", registry.GetString(RegistryRoot.User, InstallLocations.UninstallKey, "DisplayVersion"));
        Assert.Null(registry.GetString(RegistryRoot.Machine, InstallLocations.UninstallKey, "DisplayVersion"));
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
    }

    [Fact]
    public void TheSameDevCommitStopsBeforeTheZipEvenWhenTheVersionTextDiffers()
    {
        const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var feed = new ScriptedFeed(Archive, Sums(Archive), "0.1.0-dev.43+bbbbbbb", commit);

        var result = Apply(feed, "0.1.0-dev.42", MachineExe(), elevated: true, UpdateChannel.Dev, commit);

        Assert.IsType<UpdateResult.Current>(result);
        Assert.Equal(UpdateChannel.Dev, feed.Requested);
        Assert.Empty(feed.Downloads);
        Assert.Empty(session.Operations);
        Assert.Equal([8], files.Read(Path.Combine(Machine, "winland.exe")));
    }

    [Fact]
    public void ADifferentDevCommitReplacesTheInstall()
    {
        var feed = new ScriptedFeed(Archive, Sums(Archive), "0.1.0-dev.42+bbbbbbb", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        var result = Apply(
            feed,
            "0.1.0-dev.42",
            MachineExe(),
            elevated: true,
            UpdateChannel.Dev,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var updated = Assert.IsType<UpdateResult.Updated>(result);
        Assert.Equal("0.1.0-dev.42+bbbbbbb", updated.Version);
        Assert.Equal(UpdateChannel.Dev, feed.Requested);
        Assert.Equal([1], files.Read(Path.Combine(Machine, "winland.exe")));
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
    }

    [Fact]
    public void SwitchingFromDevToStableInstallsTheOlderStableRelease()
    {
        var feed = new ScriptedFeed(Archive, Sums(Archive), "0.1.0", "cccccccccccccccccccccccccccccccccccccccc");

        var result = Apply(
            feed,
            "0.1.0-dev.42",
            MachineExe(),
            elevated: true,
            UpdateChannel.Stable,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var updated = Assert.IsType<UpdateResult.Updated>(result);
        Assert.Equal("0.1.0", updated.Version);
        Assert.Equal(UpdateChannel.Stable, feed.Requested);
        Assert.Equal([1], files.Read(Path.Combine(Machine, "winland.exe")));
        Assert.Equal([9], files.Read(Path.Combine(Config, "winland.conf")));
    }

    private UpdateResult Apply(
        ScriptedFeed feed,
        string runningVersion,
        string executable,
        bool elevated,
        UpdateChannel channel,
        string runningCommit) =>
        Update(feed).Run(runningVersion, executable, Machine, User, elevated, channel, runningCommit);

    private ReleaseUpdate Update(ScriptedFeed feed, Action<string>? removed = null) =>
        new(
            feed,
            new FixedPackage(Source),
            install,
            () => @"C:\stage",
            removed ?? (_ => { }));

    private static string MachineExe() => Path.Combine(Machine, "winland.exe");

    private static string UserExe() => Path.Combine(User, "winland.exe");

    private static string Sums(byte[] archive) =>
        Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant() + "  Winland-win-x64.zip\n";

    private sealed class ScriptedFeed : IReleaseFeed
    {
        private readonly byte[] archive;
        private readonly byte[] sums;
        private readonly string version;
        private readonly string commit;

        public ScriptedFeed(byte[] archive, string sums, string version, string commit = "")
        {
            this.archive = archive;
            this.sums = System.Text.Encoding.UTF8.GetBytes(sums);
            this.version = version;
            this.commit = commit;
        }

        public int LatestCalls { get; private set; }

        public List<string> Downloads { get; } = [];

        public UpdateChannel? Requested { get; private set; }

        public ReleaseListing Latest(UpdateChannel channel)
        {
            LatestCalls++;
            Requested = channel;
            return new ReleaseListing(version, ArchiveUrl, SumsUrl, commit);
        }

        public byte[] Download(string url)
        {
            Downloads.Add(url);
            if (url == ArchiveUrl)
                return archive;

            if (url == SumsUrl)
                return sums;

            throw new InvalidOperationException(url);
        }
    }

    private sealed class FixedPackage : IReleasePackage
    {
        private readonly string source;

        public FixedPackage(string source) => this.source = source;

        public int Calls { get; private set; }

        public string Extract(byte[] archive, string directory)
        {
            Calls++;
            return source;
        }
    }
}
