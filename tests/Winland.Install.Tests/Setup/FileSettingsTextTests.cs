namespace Winland.Install.Tests;

public sealed class FileSettingsTextTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "winland-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void WriteCreatesTheFolderAndDeleteRemovesOnlyTheFile()
    {
        var path = Path.Combine(directory, ".winland", "windows-settings.json");
        var file = new FileSettingsText(path);

        Assert.Null(file.Read());

        file.Write("{\"windowArranging\":true}");

        Assert.Equal("{\"windowArranging\":true}", file.Read());
        Assert.True(Directory.Exists(Path.Combine(directory, ".winland")));

        file.Delete();

        Assert.Null(file.Read());
        Assert.True(Directory.Exists(Path.Combine(directory, ".winland")));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
