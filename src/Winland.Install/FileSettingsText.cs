namespace Winland.Install;

/// <summary>The settings record on disk. The directory is created when the file is written.</summary>
public sealed class FileSettingsText : ISettingsText
{
    private readonly string path;

    public FileSettingsText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
    }

    public string? Read() => File.Exists(path) ? File.ReadAllText(path) : null;

    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, text);
    }

    public void Delete()
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
