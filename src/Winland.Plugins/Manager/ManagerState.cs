using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland.Plugins.Manager;

/// <summary>
/// An installed repository as the manager names it: its name, the author read from its URL, and
/// whether each of its plugins is enabled.
/// </summary>
public sealed record ManagedRepository(string Url, string Name, string Author, IReadOnlyDictionary<string, bool> Enabled);

/// <summary>
/// Which installed plugins are enabled, kept in the user's staging folder. It decides only what
/// <c>reload</c> loads: a plugin still loads only when the protected approvals file lists its hash.
/// </summary>
public static class ManagerState
{
    public const string FileName = "state.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static IReadOnlyList<ManagedRepository> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            return [];

        List<Entry?>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<Entry?>>(File.ReadAllText(path), Json);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Could not read the plugin state '{path}': {exception.Message}", exception);
        }

        return (entries ?? [])
            .OfType<Entry>()
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Url))
            .Select(entry => new ManagedRepository(
                entry.Url!,
                string.IsNullOrWhiteSpace(entry.Name) ? NameOf(entry.Url!) : entry.Name,
                AuthorOf(entry.Url!),
                new Dictionary<string, bool>(entry.Plugins ?? [], StringComparer.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>Writes the file through a temporary file in the same directory, so a reader never sees half of it.</summary>
    public static void Write(string path, IReadOnlyList<ManagedRepository> repositories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(repositories);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var entries = repositories.Select(repository => new Entry
        {
            Url = repository.Url,
            Name = repository.Name,
            Plugins = repository.Enabled.ToDictionary(pair => pair.Key, pair => pair.Value),
        }).ToList();
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, Json) + Environment.NewLine);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The URL's last path segment, as the pinned plugin manager names a repository whose manifest gives no name.</summary>
    public static string NameOf(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var trimmed = url.TrimEnd('/', '\\');
        var last = trimmed.LastIndexOfAny(['/', '\\']);
        return last < 0 ? trimmed : trimmed[(last + 1)..];
    }

    /// <summary>The URL's second-to-last path segment, which the pinned plugin manager takes as the author.</summary>
    public static string AuthorOf(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var trimmed = url.TrimEnd('/', '\\');
        var last = trimmed.LastIndexOfAny(['/', '\\']);
        if (last <= 0)
            return "";

        var before = trimmed.LastIndexOfAny(['/', '\\'], last - 1);
        return trimmed[(before + 1)..last];
    }

    private sealed class Entry
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("plugins")]
        public Dictionary<string, bool>? Plugins { get; set; }
    }
}
