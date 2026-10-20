using Winland.Plugins.Manager;

namespace Winland.Tests;

public class PluginManifestTests
{
    [Fact]
    public void ManifestListsEachPluginTable()
    {
        var manifest = PluginManifest.Parse(
            """
            # A repository with two plugins.
            [repository]
            name = "samples"
            authors = ["one", 'two']

            [sample]
            description = "Echoes its argument" # trailing comment
            api_version = 1
            project = "src/Sample/Sample.csproj"

            [other]
            author = "three"
            api_version = 1
            project = 'Other\Other.csproj'
            assembly = "Other.Plugin.dll"
            """);

        Assert.Equal("samples", manifest.RepositoryName);
        Assert.Equal(["one", "two"], manifest.Authors);
        Assert.Equal(2, manifest.Plugins.Count);
        var sample = manifest.Plugins[0];
        Assert.Equal("sample", sample.Name);
        Assert.Equal(1, sample.ApiVersion);
        Assert.Equal("src/Sample/Sample.csproj", sample.Project);
        Assert.Equal("Sample.dll", sample.Assembly);
        Assert.Equal("Echoes its argument", sample.Description);
        Assert.Empty(sample.Authors);
        var other = manifest.Plugins[1];
        Assert.Equal("Other.Plugin.dll", other.Assembly);
        Assert.Equal(@"Other\Other.csproj", other.Project);
        Assert.Equal(["three"], other.Authors);
        Assert.Empty(manifest.Incompatible(1));
    }

    [Fact]
    public void AnotherApiVersionIsReportedPerPlugin()
    {
        var manifest = PluginManifest.Parse("[old]\napi_version = 2\nproject = \"Old.csproj\"\n[ok]\napi_version = 1\nproject = \"Ok.csproj\"\n");

        Assert.Equal(["Plugin 'old' declares API version 2; Winland accepts 1."], manifest.Incompatible(1));
    }

    [Theory]
    [InlineData("[repository]\nname = \"x\"\n", "lists no plugins")]
    [InlineData("[p]\nproject = \"P.csproj\"\n", "has no api_version")]
    [InlineData("[p]\napi_version = \"1\"\nproject = \"P.csproj\"\n", "not an integer")]
    [InlineData("[p]\napi_version = 1\n", "has no project and no build")]
    [InlineData("[p]\napi_version = 1\nproject = \"../P.csproj\"\n", "inside the repository")]
    [InlineData("[p]\napi_version = 1\nproject = \"C:/P.csproj\"\n", "inside the repository")]
    [InlineData("[p]\napi_version = 1\nproject = \"P.fsproj\"\n", "inside the repository")]
    [InlineData("[p]\napi_version = 1\nproject = \"P.csproj\"\nassembly = \"../P.dll\"\n", "a .dll file name")]
    [InlineData("[p]\napi_version = 1\nproject = 3\n", "must be a string")]
    [InlineData("name = \"x\"\n", "inside a table")]
    [InlineData("[p]\n[p]\n", "defined twice")]
    [InlineData("[p]\napi_version = 1\napi_version = 1\n", "set twice")]
    [InlineData("[p]\nproject = \"open\n", "not closed")]
    [InlineData("[p]\nbuild = { a = 1 }\n", "not a string, an integer")]
    [InlineData("[[p]]\n", "must be [name]")]
    [InlineData("[p.q]\n", "not a bare key")]
    [InlineData("[p]\nproject = \"P.csproj\" extra\n", "Unexpected text")]
    [InlineData("[p]\napi_version = 1\nbuild = [\"make\"]\n", "has build steps and no output")]
    [InlineData("[p]\napi_version = 1\nbuild = []\noutput = \"p.dll\"\n", "not a list of commands")]
    [InlineData("[p]\napi_version = 1\nbuild = \"make\"\noutput = \"p.dll\"\n", "not a list of commands")]
    [InlineData("[p]\napi_version = 1\nbuild = [\"make\"]\noutput = \"../p.dll\"\n", "inside the repository")]
    [InlineData("[p]\napi_version = 1\nbuild = [\"make\"]\noutput = \"p.so\"\n", "inside the repository")]
    public void InvalidManifestIsRefused(string text, string reason)
    {
        var exception = Assert.Throws<ManifestException>(() => PluginManifest.Parse(text));

        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStepsAndOutputReplaceTheProject()
    {
        var manifest = PluginManifest.Parse("[p]\napi_version = 1\nbuild = [\"dotnet build P.csproj\", \"copy x y\"]\noutput = \"bin/P.dll\"\n");

        var plugin = Assert.Single(manifest.Plugins);
        Assert.Equal(["dotnet build P.csproj", "copy x y"], plugin.Build);
        Assert.Equal("bin/P.dll", plugin.Output);
        Assert.Equal("P.dll", plugin.Assembly);
    }

    [Fact]
    public void ParseErrorNamesTheLine()
    {
        var exception = Assert.Throws<ManifestException>(() => PluginManifest.Parse("[p]\napi_version = 1\nproject = nope\n"));

        Assert.StartsWith("line 3:", exception.Message, StringComparison.Ordinal);
    }
}
