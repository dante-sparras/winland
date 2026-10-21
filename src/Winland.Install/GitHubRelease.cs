using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Winland.Install;

/// <summary>The newest published release: its version, and where the zip and the checksums are.</summary>
public sealed record ReleaseListing(string Version, string ArchiveUrl, string ChecksumsUrl);

/// <summary>Reads a published release. Tests supply their own. No test calls GitHub.</summary>
public interface IReleaseFeed
{
    ReleaseListing Latest();

    byte[] Download(string url);
}

public sealed record ParsedRelease(string ArchiveUrl, string ChecksumsUrl, string VersionUrl);

/// <summary>The JSON of a GitHub release and of its <c>version.json</c>.</summary>
public static class GitHubReleaseParser
{
    public static ParsedRelease ReadRelease(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (Flag(root, "draft") || Flag(root, "prerelease"))
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
/// published release, and each file is fetched from the URL that release names.
/// </summary>
public sealed class GitHubReleaseFeed : IReleaseFeed
{
    private readonly HttpClient http;

    public GitHubReleaseFeed(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        this.http = http;
    }

    public ReleaseListing Latest()
    {
        ParsedRelease release;
        try
        {
            release = GitHubReleaseParser.ReadRelease(GetText(ReleaseAssets.LatestUrl, github: true));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The published release could not be read.");
        }

        try
        {
            var version = GitHubReleaseParser.ReadVersion(GetText(release.VersionUrl, github: false));
            return new ReleaseListing(version, release.ArchiveUrl, release.ChecksumsUrl);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The published release's version.json has no version.");
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
            if (code == 404 && github)
                throw new InvalidOperationException("No published release was found.");

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("The published release could not be read (HTTP " + code + ").");

            using var stream = response.Content.ReadAsStream();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
