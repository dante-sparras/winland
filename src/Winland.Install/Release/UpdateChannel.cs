namespace Winland.Install;

/// <summary>
/// Which published release <c>winland update</c> follows.
/// Stable is the newest published release. Dev is the rolling prerelease on the tag <c>dev</c>.
/// </summary>
public enum UpdateChannel
{
    Stable,
    Dev,
}

/// <summary>
/// The channel saved in <c>%USERPROFILE%\.winland\channel</c>. A plain install leaves it unset, which means stable.
/// <c>install.ps1 -Channel dev</c> runs <c>winland update --channel dev</c>, which saves dev.
/// </summary>
public static class UpdateChannelFile
{
    public const string Name = "channel";

    public static string PathFor(string userProfile) => Path.Combine(InstallLocations.Config(userProfile), Name);

    /// <summary>The flag wins. With no flag, the saved word is used, and a missing file means stable.</summary>
    public static UpdateChannel Choose(UpdateChannel? requested, string? saved) =>
        requested ?? Read(saved);

    public static UpdateChannel Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return UpdateChannel.Stable;

        var word = text.Trim().TrimStart('\uFEFF').Trim();
        if (word.Length == 0)
            return UpdateChannel.Stable;
        if (word.Equals("stable", StringComparison.OrdinalIgnoreCase))
            return UpdateChannel.Stable;

        if (word.Equals("dev", StringComparison.OrdinalIgnoreCase))
            return UpdateChannel.Dev;

        throw new InvalidOperationException("The saved update channel is not stable or dev.");
    }

    public static string Text(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Stable => "stable\n",
        UpdateChannel.Dev => "dev\n",
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };
}
