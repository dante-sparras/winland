namespace Winland.Daemon;

/// <summary>Reads the config file, and every file it sources, into a document.</summary>
static class ConfigFile
{
    public static ConfigDocument Read(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full) ?? ".";
        var source = new FileConfigSource(directory);
        var text = source.Read(full)
            ?? throw new InvalidOperationException($"Could not read config '{full}'.");

        return ConfigParser.Parse(text, source, KeyboardLayout.Active(), full);
    }

    /// <summary>A reload's read. A file that is gone or does not read keeps the config in force, and the warning says why.</summary>
    public static ConfigRead TryRead(string path)
    {
        try
        {
            return new ConfigRead(Read(path));
        }
        catch (InvalidOperationException exception)
        {
            return new ConfigRead(null, new ConfigWarning(0, exception.Message, Path.GetFullPath(path)));
        }
    }
}
