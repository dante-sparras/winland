using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland.Plugins.Manager;

/// <summary>
/// One entry of the approvals file. The manager adds where the assembly was installed from;
/// an entry approved by hand has no repository.
/// </summary>
public sealed record InstalledPlugin(string Name, string Sha256, string? File, string? Repository, string? Commit);

/// <summary>
/// Reads and writes the approvals file with the fields the manager records. The host reads
/// only <c>name</c> and <c>sha256</c> from the same file.
/// </summary>
public static class InstalledPlugins
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IReadOnlyList<InstalledPlugin> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!System.IO.File.Exists(path))
            return [];

        return Parse(() => System.IO.File.ReadAllText(path), path);
    }

    /// <summary>Reads the approvals from a file that is already open. <paramref name="path"/> names it in errors.</summary>
    public static IReadOnlyList<InstalledPlugin> Read(Stream stream, string path)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(
            () =>
            {
                using var reader = new StreamReader(stream, leaveOpen: true);
                return reader.ReadToEnd();
            },
            path);
    }

    private static InstalledPlugin[] Parse(Func<string> read, string path)
    {
        List<Entry?>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<Entry?>>(read(), Json);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Could not read approvals '{path}': {exception.Message}", exception);
        }

        return (entries ?? [])
            .OfType<Entry>()
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.Sha256))
            .Select(entry => new InstalledPlugin(
                entry.Name!.Trim(),
                entry.Sha256!.Trim().ToLowerInvariant(),
                Blank(entry.File),
                Blank(entry.Repository),
                Blank(entry.Commit)))
            .ToArray();
    }

    /// <summary>Writes the file through a temporary file in the same directory, so a reader never sees half of it.</summary>
    public static void Write(string path, IReadOnlyList<InstalledPlugin> plugins)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(plugins);
        var entries = plugins.Select(plugin => new Entry
        {
            Name = plugin.Name,
            Sha256 = plugin.Sha256,
            File = plugin.File,
            Repository = plugin.Repository,
            Commit = plugin.Commit,
        }).ToList();
        var temporary = path + ".tmp";
        System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(entries, Json) + Environment.NewLine);
        System.IO.File.Move(temporary, path, overwrite: true);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class Entry
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("file")]
        public string? File { get; set; }

        [JsonPropertyName("repository")]
        public string? Repository { get; set; }

        [JsonPropertyName("commit")]
        public string? Commit { get; set; }
    }
}
