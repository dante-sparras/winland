using System.Text;
using System.Text.Json;

namespace Winland.Install;

/// <summary>
/// A release to install: its version, where the zip and the checksums are, and the commit for the dev channel.
/// Stable leaves <see cref="Commit"/> empty and compares versions.
/// </summary>
public sealed record ReleaseListing(string Version, string ArchiveUrl, string ChecksumsUrl, string Commit = "");

/// <summary>Reads a published release. Tests supply their own. No test calls GitHub.</summary>
public interface IReleaseFeed
{
    ReleaseListing Latest(UpdateChannel channel);

    byte[] Download(string url);
}

public sealed record ParsedRelease(string ArchiveUrl, string ChecksumsUrl, string VersionUrl);

/// <summary>The JSON of a GitHub release and of its <c>version.json</c>.</summary>
public static class GitHubReleaseParser
{
    public static ParsedRelease ReadRelease(string json, bool allowPrerelease = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (Flag(root, "draft"))
            throw new InvalidOperationException(allowPrerelease
                ? "The dev release is a draft."
                : "The latest release is not a published release.");

        if (!allowPrerelease && Flag(root, "prerelease"))
            throw new InvalidOperationException("The latest release is not a published release.");

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The published release has no assets.");

        string? archive = null;
        string? sums = null;
        string? version = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = Text(asset, "name");
            var url = Text(asset, "browser_download_url");
            if (name is null)
                continue;

            if (name.Equals(ReleaseAssets.ArchiveName, StringComparison.Ordinal))
                archive = Https(url, ReleaseAssets.ArchiveName);
            else if (name.Equals(ReleaseAssets.ChecksumsName, StringComparison.Ordinal))
                sums = Https(url, ReleaseAssets.ChecksumsName);
            else if (name.Equals(ReleaseAssets.VersionName, StringComparison.Ordinal))
                version = Https(url, ReleaseAssets.VersionName);
        }

        if (archive is null)
            throw new InvalidOperationException("The published release has no " + ReleaseAssets.ArchiveName + ".");

        if (sums is null)
            throw new InvalidOperationException("The published release has no " + ReleaseAssets.ChecksumsName + ".");

        if (version is null)
            throw new InvalidOperationException("The published release has no " + ReleaseAssets.VersionName + ".");

        return new ParsedRelease(archive, sums, version);
    }

    public static string ReadVersion(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var version = Text(document.RootElement, "version");
        if (version is null || version.Length > 64 || version.AsSpan().IndexOfAny("\"\r\n") >= 0)
            throw new InvalidOperationException("The published release's version.json has no version.");

        return version;
    }

    /// <summary>The <c>commit</c> in <c>version.json</c>, or null when that field is absent.</summary>
    public static string? ReadCommit(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var commit = Text(document.RootElement, "commit");
        if (commit is null)
            return null;

        if (commit.Length > 64 || commit.AsSpan().IndexOfAny("\"\r\n") >= 0)
            throw new InvalidOperationException("The dev release's version.json has no commit.");

        return commit;
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string Https(string? url, string name)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The published release has an unsupported download for " + name + ".");

        return uri.AbsoluteUri;
    }
}

/// <summary>
/// The GitHub release API, called with no credentials. <see cref="ReleaseAssets.LatestUrl"/> is the newest
/// published release. <see cref="ReleaseAssets.DevUrl"/> is the rolling prerelease on the tag <c>dev</c>.
/// Each file is fetched from the URL that release names.
/// </summary>
public sealed class GitHubReleaseFeed : IReleaseFeed
{
    private readonly HttpClient http;

    public GitHubReleaseFeed(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        this.http = http;
    }

    public ReleaseListing Latest(UpdateChannel channel)
    {
        if (channel is not UpdateChannel.Stable and not UpdateChannel.Dev)
            throw new ArgumentOutOfRangeException(nameof(channel));

        var dev = channel == UpdateChannel.Dev;
        var url = dev ? ReleaseAssets.DevUrl : ReleaseAssets.LatestUrl;
        ParsedRelease release;
        try
        {
            release = GitHubReleaseParser.ReadRelease(GetText(url, github: true), allowPrerelease: dev);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(dev
                ? "The dev release could not be read."
                : "The published release could not be read.");
        }

        try
        {
            var body = GetText(release.VersionUrl, github: false);
            var version = GitHubReleaseParser.ReadVersion(body);
            var commit = "";
            if (dev)
            {
                commit = GitHubReleaseParser.ReadCommit(body)
                    ?? throw new InvalidOperationException("The dev release's version.json has no commit.");
            }

            return new ReleaseListing(version, release.ArchiveUrl, release.ChecksumsUrl, commit);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(dev
                ? "The dev release's version.json has no version."
                : "The published release's version.json has no version.");
        }
    }

    public byte[] Download(string url) => GetBytes(url, github: false);

    private string GetText(string url, bool github) => Encoding.UTF8.GetString(GetBytes(url, github));

    private byte[] GetBytes(string url, bool github)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The release feed refused a download that is not https.");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        // GitHub refuses a request that names no user agent. Nothing here sends a token.
        request.Headers.TryAddWithoutValidation("User-Agent", ReleaseAssets.UserAgent);
        request.Headers.TryAddWithoutValidation(
            "Accept",
            github ? "application/vnd.github+json" : "application/octet-stream");

        HttpResponseMessage response;
        try
        {
            response = http.Send(request);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("The published release could not be read: " + exception.Message);
        }

        using (response)
        {
            var code = (int)response.StatusCode;
            if (code is 403 or 429
                && response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining)
                && remaining.Contains("0"))
            {
                var reset = "Try again later.";
                if (response.Headers.TryGetValues("x-ratelimit-reset", out var values)
                    && long.TryParse(values.FirstOrDefault(), System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                    && seconds is >= 0 and <= 253402300799)
                {
                    reset = "Try again after " + DateTimeOffset.FromUnixTimeSeconds(seconds)
                        .ToString("yyyy-MM-dd HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture) + ".";
                }

                throw new InvalidOperationException("The GitHub API rate limit is used up. " + reset);
            }

            if (code == 404 && github)
            {
                throw new InvalidOperationException(url == ReleaseAssets.DevUrl
                    ? "No dev release was found."
                    : "No published release was found.");
            }

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("The published release could not be read (HTTP " + code + ").");

            using var stream = response.Content.ReadAsStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
