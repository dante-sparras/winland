using System.Net;
using System.Text;

namespace Winland.Install.Tests;

public class GitHubReleaseTests
{
    private const string ZipUrl = "https://github.com/dante-sparras/winland/releases/download/v0.1.0/Winland-win-x64.zip";

    private const string SumsUrl = "https://github.com/dante-sparras/winland/releases/download/v0.1.0/SHA256SUMS";

    private const string VersionUrl = "https://github.com/dante-sparras/winland/releases/download/v0.1.0/version.json";

    [Fact]
    public void TheReleaseJsonNamesTheThreeAssets()
    {
        var parsed = GitHubReleaseParser.ReadRelease(ReleaseJson());

        Assert.Equal(ZipUrl, parsed.ArchiveUrl);
        Assert.Equal(SumsUrl, parsed.ChecksumsUrl);
        Assert.Equal(VersionUrl, parsed.VersionUrl);
        Assert.Equal("0.1.0", GitHubReleaseParser.ReadVersion("{\"commit\":\"abc\",\"date\":\"2026-10-21T00:00:00Z\",\"version\":\"0.1.0\"}\n"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ADraftOrPrereleaseIsRefused(bool draft, bool prerelease)
    {
        var error = Assert.Throws<InvalidOperationException>(() => GitHubReleaseParser.ReadRelease(ReleaseJson(draft, prerelease)));

        Assert.Contains("not a published release", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnHttpDownloadIsRefused()
    {
        var json = ReleaseJson(zip: "http://github.com/dante-sparras/winland/releases/download/v0.1.0/Winland-win-x64.zip");

        var error = Assert.Throws<InvalidOperationException>(() => GitHubReleaseParser.ReadRelease(json));

        Assert.Contains("unsupported download", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReleaseWithoutTheZipIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GitHubReleaseParser.ReadRelease(ReleaseJson(zip: null)));

        Assert.Contains("Winland-win-x64.zip", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionJsonWithoutAVersionIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GitHubReleaseParser.ReadVersion("{\"commit\":\"abc\"}"));

        Assert.Contains("no version", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LatestReadsTheApiThenVersionJsonAndNamesNoToken()
    {
        // The handler answers every call. An unexpected URL fails here, so the test never reaches GitHub.
        var handler = new RecordingHandler(url =>
        {
            if (url == ReleaseAssets.LatestUrl)
                return Text(ReleaseJson());

            if (url == VersionUrl)
                return Text("{\"commit\":\"abc\",\"date\":\"2026-10-21T00:00:00Z\",\"version\":\"0.2.0\"}\n");

            throw new InvalidOperationException(url);
        });
        using var http = new HttpClient(handler);

        var listing = new GitHubReleaseFeed(http).Latest(UpdateChannel.Stable);

        Assert.Equal("0.2.0", listing.Version);
        Assert.Equal(ZipUrl, listing.ArchiveUrl);
        Assert.Equal(SumsUrl, listing.ChecksumsUrl);
        Assert.Equal([ReleaseAssets.LatestUrl, VersionUrl], handler.Calls.Select(call => call.Url).ToArray());
        Assert.All(handler.Calls, call =>
        {
            Assert.Equal(ReleaseAssets.UserAgent, call.UserAgent);
            Assert.False(call.Authorized);
        });
        Assert.Equal("application/vnd.github+json", handler.Calls[0].Accept);
    }

    [Fact]
    public void DownloadFetchesTheNamedUrl()
    {
        var handler = new RecordingHandler(url =>
        {
            if (url == ZipUrl)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };

            throw new InvalidOperationException(url);
        });
        using var http = new HttpClient(handler);

        var bytes = new GitHubReleaseFeed(http).Download(ZipUrl);

        Assert.Equal([1, 2, 3], bytes);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(ReleaseAssets.UserAgent, call.UserAgent);
        Assert.False(call.Authorized);
        Assert.Equal("application/octet-stream", call.Accept);
    }

    [Fact]
    public void AMissingReleaseIsRefusedBeforeAnyFileIsRead()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);

        var error = Assert.Throws<InvalidOperationException>(() => new GitHubReleaseFeed(http).Latest(UpdateChannel.Stable));

        Assert.Contains("No published release", error.Message, StringComparison.Ordinal);
        Assert.Equal(ReleaseAssets.LatestUrl, Assert.Single(handler.Calls).Url);
    }

    [Fact]
    public void APrereleaseResponseDoesNotDownloadTheZip()
    {
        var handler = new RecordingHandler(url =>
        {
            if (url == ReleaseAssets.LatestUrl)
                return Text(ReleaseJson(prerelease: true));

            throw new InvalidOperationException(url);
        });
        using var http = new HttpClient(handler);

        var error = Assert.Throws<InvalidOperationException>(() => new GitHubReleaseFeed(http).Latest(UpdateChannel.Stable));

        Assert.Contains("not a published release", error.Message, StringComparison.Ordinal);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public void MalformedReleaseJsonIsRefused()
    {
        var handler = new RecordingHandler(_ => Text("not-json"));
        using var http = new HttpClient(handler);

        var error = Assert.Throws<InvalidOperationException>(() => new GitHubReleaseFeed(http).Latest(UpdateChannel.Stable));

        Assert.Contains("could not be read", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DevReadsTheTagReleaseAndItsCommit()
    {
        var handler = new RecordingHandler(url =>
        {
            if (url == ReleaseAssets.DevUrl)
                return Text(ReleaseJson(prerelease: true));

            if (url == VersionUrl)
                return Text("{\"commit\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"date\":\"2026-10-21T00:00:00Z\",\"version\":\"0.1.0-dev.42+aaaaaaa\"}\n");

            throw new InvalidOperationException(url);
        });
        using var http = new HttpClient(handler);

        var listing = new GitHubReleaseFeed(http).Latest(UpdateChannel.Dev);

        Assert.Equal("0.1.0-dev.42+aaaaaaa", listing.Version);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", listing.Commit);
        Assert.Equal([ReleaseAssets.DevUrl, VersionUrl], handler.Calls.Select(call => call.Url).ToArray());
        Assert.All(handler.Calls, call => Assert.False(call.Authorized));
    }

    [Fact]
    public void ADevReleaseWithoutACommitDoesNotDownloadTheZip()
    {
        var handler = new RecordingHandler(url =>
        {
            if (url == ReleaseAssets.DevUrl)
                return Text(ReleaseJson(prerelease: true));

            if (url == VersionUrl)
                return Text("{\"date\":\"2026-10-21T00:00:00Z\",\"version\":\"0.1.0-dev.1+aaaaaaa\"}\n");

            throw new InvalidOperationException(url);
        });
        using var http = new HttpClient(handler);

        var error = Assert.Throws<InvalidOperationException>(() => new GitHubReleaseFeed(http).Latest(UpdateChannel.Dev));

        Assert.Contains("no commit", error.Message, StringComparison.Ordinal);
        Assert.Equal([ReleaseAssets.DevUrl, VersionUrl], handler.Calls.Select(call => call.Url).ToArray());
    }

    [Fact]
    public void AnExhaustedRateLimitNamesItsResetTime()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", "1793145600");
            return response;
        });
        using var http = new HttpClient(handler);

        var error = Assert.Throws<InvalidOperationException>(() => new GitHubReleaseFeed(http).Latest(UpdateChannel.Stable));

        Assert.Contains("GitHub API rate limit is used up", error.Message, StringComparison.Ordinal);
        Assert.Contains("2026-10-28 00:00:00 UTC", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingDevReleaseIsRefused()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);

        var error = Assert.Throws<InvalidOperationException>(() => new GitHubReleaseFeed(http).Latest(UpdateChannel.Dev));

        Assert.Contains("No dev release", error.Message, StringComparison.Ordinal);
        Assert.Equal(ReleaseAssets.DevUrl, Assert.Single(handler.Calls).Url);
    }

    [Fact]
    public void APrereleaseOnTheDevTagIsAcceptedAndADraftIsNot()
    {
        var parsed = GitHubReleaseParser.ReadRelease(ReleaseJson(prerelease: true), allowPrerelease: true);

        Assert.Equal(ZipUrl, parsed.ArchiveUrl);
        var error = Assert.Throws<InvalidOperationException>(() =>
            GitHubReleaseParser.ReadRelease(ReleaseJson(draft: true), allowPrerelease: true));
        Assert.Contains("draft", error.Message, StringComparison.Ordinal);
    }

    private static string ReleaseJson(bool draft = false, bool prerelease = false, string? zip = ZipUrl)
    {
        var assets = new List<string>
        {
            Asset(ReleaseAssets.ChecksumsName, SumsUrl),
            Asset(ReleaseAssets.VersionName, VersionUrl),
        };
        if (zip is not null)
            assets.Insert(0, Asset(ReleaseAssets.ArchiveName, zip));

        return "{\"draft\":" + (draft ? "true" : "false")
            + ",\"prerelease\":" + (prerelease ? "true" : "false")
            + ",\"assets\":[" + string.Join(',', assets) + "]}";
    }

    private static string Asset(string name, string url) =>
        "{\"name\":\"" + name + "\",\"browser_download_url\":\"" + url + "\"}";

    private static HttpResponseMessage Text(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> respond;

        public RecordingHandler(Func<string, HttpResponseMessage> respond)
        {
            this.respond = respond;
        }

        public List<Call> Calls { get; } = [];

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Answer(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Answer(request));

        private HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var url = request.RequestUri?.AbsoluteUri ?? "";
            Calls.Add(new Call(
                url,
                request.Headers.TryGetValues("User-Agent", out var agents) ? string.Join(' ', agents) : null,
                request.Headers.Contains("Authorization"),
                request.Headers.TryGetValues("Accept", out var accept) ? string.Join(' ', accept) : null));
            return respond(url);
        }
    }

    private readonly record struct Call(string Url, string? UserAgent, bool Authorized, string? Accept);
}
