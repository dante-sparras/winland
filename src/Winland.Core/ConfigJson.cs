using System.Text.Json;
using System.Text.Json.Nodes;

namespace Winland.Core;

/// <summary>
/// Loads a JSON config file that may name other files with a top-level <c>include</c> array.
/// Included files load in order and merge key by key. Objects merge recursively. Arrays and scalars replace.
/// A <c>null</c> removes a key. The file that includes the others applies last, so it can add, replace, or remove one key.
/// A <c>chords</c> array merges per chord instead of replacing: a later entry for the same <c>chord</c> replaces the earlier one,
/// and an entry whose <c>command</c> is <c>null</c> removes that chord. The first entry for a chord inside one array wins.
/// A path is relative to the file that names it. A cycle is skipped. A missing include is noted and skipped.
/// </summary>
public static class ConfigJson
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The merged document and the notes a caller logs. A missing include is a note, not a failure.</summary>
    /// <param name="Json">The merged object, indented.</param>
    /// <param name="Notes">One line for each include that was skipped.</param>
    public readonly record struct ReadResult(string Json, IReadOnlyList<string> Notes);

    /// <summary>
    /// Reads <paramref name="path"/> and every file it includes. A missing root file is an empty object and a note.
    /// A root file that is not JSON throws, so the caller can keep the previous document.
    /// </summary>
    /// <param name="path">The file the person edited.</param>
    /// <returns>The merge, with <c>include</c> consumed.</returns>
    /// <exception cref="JsonException">The root file is not JSON.</exception>
    public static ReadResult Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var notes = new List<string>();
        if (!File.Exists(full))
        {
            notes.Add("missing " + full);
            return new ReadResult("{}", notes);
        }

        var chain = new Chain();
        var merged = ReadObject(full, chain, notes, root: true);
        return new ReadResult(merged.ToJsonString(Indented), notes);
    }

    /// <summary>Merges <paramref name="upper"/> onto <paramref name="lower"/> by the rules of <see cref="Read"/>.</summary>
    /// <param name="lower">The document so far. Included files are the lower one.</param>
    /// <param name="upper">The document that applies next.</param>
    /// <returns>The merged object, indented.</returns>
    /// <exception cref="JsonException">Either text is not JSON.</exception>
    public static string Merge(string lower, string upper)
    {
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(upper);
        var left = AsObject(lower);
        var right = AsObject(upper);
        return MergeObjects(left, right).ToJsonString(Indented);
    }

    private static JsonObject ReadObject(string full, Chain chain, List<string> notes, bool root)
    {
        if (chain.Contains(full))
        {
            notes.Add("include cycle " + full);
            return new JsonObject();
        }

        if (!File.Exists(full))
        {
            notes.Add("missing include " + full);
            return new JsonObject();
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(File.ReadAllText(full));
        }
        catch (JsonException ex)
        {
            if (root)
            {
                throw;
            }

            notes.Add("include is not json " + full + ": " + ex.Message);
            return new JsonObject();
        }

        if (parsed is not JsonObject obj)
        {
            if (root)
            {
                notes.Add("not an object " + full);
                return new JsonObject();
            }

            notes.Add("include is not an object " + full);
            return new JsonObject();
        }

        chain.Push(full);
        var includes = IncludePaths(obj, full);
        obj.Remove("include");
        var merged = new JsonObject();
        foreach (var include in includes)
        {
            merged = MergeObjects(merged, ReadObject(include, chain, notes, root: false));
        }

        merged = MergeObjects(merged, obj);
        chain.Pop();
        return merged;
    }

    private static List<string> IncludePaths(JsonObject obj, string full)
    {
        var paths = new List<string>();
        if (obj["include"] is not JsonArray includes)
        {
            return paths;
        }

        var folder = Path.GetDirectoryName(full)!;
        foreach (var item in includes)
        {
            if (item is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            paths.Add(Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(folder, text)));
        }

        return paths;
    }

    private static JsonObject AsObject(string json)
    {
        if (json.Trim().Length == 0)
        {
            return new JsonObject();
        }

        return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
    }

    private static JsonObject MergeObjects(JsonObject lower, JsonObject upper)
    {
        var result = (JsonObject)lower.DeepClone();
        foreach (var item in upper)
        {
            if (item.Value is null)
            {
                result.Remove(item.Key);
                continue;
            }

            if (item.Key.Equals("chords", StringComparison.Ordinal)
                && item.Value is JsonArray upperChords
                && result[item.Key] is JsonArray lowerChords)
            {
                result[item.Key] = MergeChords(lowerChords, upperChords);
                continue;
            }

            if (item.Value is JsonObject upperObject && result[item.Key] is JsonObject lowerObject)
            {
                result[item.Key] = MergeObjects(lowerObject, upperObject);
                continue;
            }

            result[item.Key] = item.Value.DeepClone();
        }

        return result;
    }

    private static JsonArray MergeChords(JsonArray lower, JsonArray upper)
    {
        var order = new List<string>();
        var entries = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        Absorb(lower, entries, order, replace: false);
        Absorb(upper, entries, order, replace: true);
        var merged = new JsonArray();
        foreach (var chord in order)
        {
            merged.Add((JsonNode)entries[chord]);
        }

        return merged;
    }

    private static void Absorb(JsonArray array, Dictionary<string, JsonObject> entries, List<string> order, bool replace)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array)
        {
            if (item is not JsonObject entry)
            {
                continue;
            }

            var chord = ChordOf(entry);
            if (chord is null || !seen.Add(chord))
            {
                continue;
            }

            if (entry.TryGetPropertyValue("command", out var command) && command is null)
            {
                if (entries.Remove(chord))
                {
                    order.Remove(chord);
                }

                continue;
            }

            if (!replace && entries.ContainsKey(chord))
            {
                continue;
            }

            if (!entries.ContainsKey(chord))
            {
                order.Add(chord);
            }

            entries[chord] = (JsonObject)entry.DeepClone();
        }
    }

    private static string? ChordOf(JsonObject entry)
    {
        if (!entry.TryGetPropertyValue("chord", out var node) || node is not JsonValue value || !value.TryGetValue<string>(out var chord))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(chord) ? null : chord;
    }

    private sealed class Chain
    {
        private readonly Stack<string> _stack = new();
        private readonly HashSet<string> _set = new(StringComparer.OrdinalIgnoreCase);

        public bool Contains(string path) => _set.Contains(path);

        public void Push(string path)
        {
            _stack.Push(path);
            _set.Add(path);
        }

        public void Pop() => _set.Remove(_stack.Pop());
    }
}
