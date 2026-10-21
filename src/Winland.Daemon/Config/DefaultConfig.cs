using System.Text;

namespace Winland.Daemon;

/// <summary>
/// The config Winland writes on first start. It is the embedded <c>winland.default.conf</c>.
/// </summary>
static class DefaultConfig
{
    public const string ResourceName = "winland.default.conf";

    /// <summary>The embedded default, comments included.</summary>
    public static string Text { get; } = Read();

    /// <summary>
    /// Writes <see cref="Text"/> to <paramref name="path"/> when that file is absent.
    /// An existing file is left as it is. Returns whether this call wrote the file.
    /// </summary>
    public static bool WriteIfMissing(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        try
        {
            using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(Text);
            return true;
        }
        catch (IOException) when (File.Exists(full))
        {
            return false;
        }
    }

    private static string Read()
    {
        var stream = typeof(DefaultConfig).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The default config is not embedded.");
        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            return reader.ReadToEnd();
    }
}
