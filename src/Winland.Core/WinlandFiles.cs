namespace Winland.Core;

/// <summary>
/// Writes a person's Winland file that includes the copy Winoma shipped, when the person has no file yet.
/// Winland alone, with no shipped copy beside the program, starts from the built-in defaults and writes nothing here.
/// </summary>
public static class WinlandFiles
{
    /// <summary>
    /// Ensures <c>binds.json</c> and <c>settings.json</c> exist in <see cref="ConfigFolder"/> when this program has shipped copies.
    /// A file the person already has is left as it is.
    /// </summary>
    /// <param name="baseDirectory">The program's folder, <see cref="AppContext.BaseDirectory"/>.</param>
    public static void Ensure(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var shipped = Path.Combine(baseDirectory, "defaults", "winland");
        Seed(Path.Combine(shipped, Chords.ChordBook.FileName), Path.Combine(ConfigFolder.Path, Chords.ChordBook.FileName));
        Seed(Path.Combine(shipped, TileSettingsFile.FileName), Path.Combine(ConfigFolder.Path, TileSettingsFile.FileName));
    }

    private static void Seed(string shipped, string person)
    {
        if (!File.Exists(shipped) || File.Exists(person))
        {
            return;
        }

        var folder = Path.GetDirectoryName(person);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        AtomicFile.Write(person, "{\n  \"include\": [\n    " + Quote(Path.GetFullPath(shipped)) + "\n  ]\n}\n");
    }

    private static string Quote(string text)
    {
        var buffer = new System.Text.StringBuilder(text.Length + 2);
        buffer.Append('"');
        foreach (var character in text)
        {
            switch (character)
            {
                case '\\':
                    buffer.Append("\\\\");
                    break;
                case '"':
                    buffer.Append("\\\"");
                    break;
                default:
                    buffer.Append(character);
                    break;
            }
        }

        buffer.Append('"');
        return buffer.ToString();
    }
}
