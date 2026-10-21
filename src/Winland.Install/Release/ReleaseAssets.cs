namespace Winland.Install;

/// <summary>The files on a published Winland release, and where the newest one is read from.</summary>
public static class ReleaseAssets
{
    public const string ArchiveName = "Winland-win-x64.zip";

    public const string ChecksumsName = "SHA256SUMS";

    public const string VersionName = "version.json";

    /// <summary>The newest published, non-prerelease release. GitHub's <c>latest</c> is that release.</summary>
    public const string LatestUrl = "https://api.github.com/repos/dante-sparras/winland/releases/latest";

    /// <summary>The rolling dev prerelease. GitHub's <c>latest</c> does not return it.</summary>
    public const string DevUrl = "https://api.github.com/repos/dante-sparras/winland/releases/tags/dev";

    public const string UserAgent = "winland";
}
