using System.Text;

namespace Winland.Install;

/// <summary>What <c>winland update</c> decided. The caller prints it and, when asked, relaunches elevated.</summary>
public abstract record UpdateResult
{
    private UpdateResult()
    {
    }

    public sealed record NotInstalled : UpdateResult;

    public sealed record Current : UpdateResult;

    /// <summary>The machine install needs one approval before the files are replaced. The zip has already been checked.</summary>
    public sealed record NeedsAdministrator : UpdateResult;

    public sealed record Rejected : UpdateResult;

    public sealed record Updated(string Version) : UpdateResult;
}

/// <summary>
/// Reads the newest published release and, when it differs, replaces the install this process was started from.
/// The copy, the stop and the start are <see cref="AppInstall"/>'s.
/// </summary>
public sealed class ReleaseUpdate
{
    private readonly IReleaseFeed feed;
    private readonly IReleasePackage package;
    private readonly AppInstall install;
    private readonly Func<string> stageDirectory;
    private readonly Action<string> removeStage;

    public ReleaseUpdate(
        IReleaseFeed feed,
        IReleasePackage package,
        AppInstall install,
        Func<string> stageDirectory,
        Action<string> removeStage)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(stageDirectory);
        ArgumentNullException.ThrowIfNull(removeStage);
        this.feed = feed;
        this.package = package;
        this.install = install;
        this.stageDirectory = stageDirectory;
        this.removeStage = removeStage;
    }

    public UpdateResult Run(
        string runningVersion,
        string executable,
        string machineDirectory,
        string userDirectory,
        bool elevated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runningVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);

        var kind = InstalledCopy.Of(executable, machineDirectory, userDirectory);
        if (kind == InstallKind.None)
            return new UpdateResult.NotInstalled();

        var listing = feed.Latest();
        if (ReleaseVersion.IsCurrent(runningVersion, listing.Version))
            return new UpdateResult.Current();

        var archive = feed.Download(listing.ArchiveUrl);
        var sums = Encoding.UTF8.GetString(feed.Download(listing.ChecksumsUrl));
        if (!ReleaseChecksum.Matches(archive, sums))
            return new UpdateResult.Rejected();

        // The approval is for the replace. A current install, a rejected zip and a per-user install never ask.
        if (kind == InstallKind.Machine && !elevated)
            return new UpdateResult.NeedsAdministrator();

        var directory = stageDirectory();
        try
        {
            var source = package.Extract(archive, directory);
            var perUser = kind == InstallKind.User;
            var destination = perUser ? userDirectory : machineDirectory;
            install.ReplaceInstalled(source, destination, perUser, listing.Version);
            return new UpdateResult.Updated(listing.Version);
        }
        finally
        {
            try
            {
                removeStage(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
