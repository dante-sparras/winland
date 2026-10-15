using System.Globalization;
using System.Text;

namespace Winland.Plugins.Manager;

/// <summary>
/// Reads the subset of TOML a plugin manifest uses: <c>[table]</c> headers, and keys whose
/// value is a basic or literal string, an integer, a boolean or an array of strings.
/// Anything else is an error, so a manifest never means more than the manager reads.
/// </summary>
internal static class ManifestToml
{
    public static ManifestTables Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tables = new List<ManifestTable>();
        var root = new ManifestTable("", 0);
        var current = root;
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var number = index + 1;
            var line = lines[index];
            var position = SkipSpace(line, 0);
            if (position >= line.Length || line[position] == '#')
                continue;

            if (line[position] == '[')
            {
                var close = line.IndexOf(']', position);
                if (close < 0 || line.AsSpan(position + 1).StartsWith("["))
                    return ManifestTables.Fail(number, "A table header must be [name].");

                var name = line[(position + 1)..close].Trim();
                if (!IsBareKey(name))
                    return ManifestTables.Fail(number, $"Table name '{name}' is not a bare key.");

                if (!AtEnd(line, close + 1))
                    return ManifestTables.Fail(number, "Unexpected text after the table header.");

                if (tables.Exists(table => table.Name == name))
                    return ManifestTables.Fail(number, $"Table [{name}] is defined twice.");

                current = new ManifestTable(name, number);
                tables.Add(current);
                continue;
            }

            var equals = line.IndexOf('=', position);
            if (equals < 0)
                return ManifestTables.Fail(number, "Expected key = value.");

            var key = line[position..equals].Trim();
            if (!IsBareKey(key))
                return ManifestTables.Fail(number, $"Key '{key}' is not a bare key.");

            if (current.Values.ContainsKey(key))
                return ManifestTables.Fail(number, $"Key '{key}' is set twice.");

            var start = SkipSpace(line, equals + 1);
            if (!TryValue(line, ref start, out var value, out var error))
                return ManifestTables.Fail(number, error);

            if (!AtEnd(line, start))
                return ManifestTables.Fail(number, "Unexpected text after the value.");

            current.Values[key] = value;
        }

        if (root.Values.Count > 0)
            return ManifestTables.Fail(1, "Every key must be inside a table.");

        return new ManifestTables(tables, null);
    }

    private static bool TryValue(string line, ref int position, out object value, out string error)
    {
        value = "";
        error = "";
        if (position >= line.Length)
        {
            error = "A value is missing.";
            return false;
        }

        var ch = line[position];
        if (ch is '"' or '\'')
        {
            if (!TryString(line, ref position, out var text, out error))
                return false;

            value = text;
            return true;
        }

        if (ch == '[')
        {
            position++;
            var items = new List<string>();
            while (true)
            {
                position = SkipSpace(line, position);
                if (position < line.Length && line[position] == ']')
                {
                    position++;
                    value = items;
                    return true;
                }

                if (!TryString(line, ref position, out var item, out error))
                {
                    error = "An array holds only strings on one line.";
                    return false;
                }

                items.Add(item);
                position = SkipSpace(line, position);
                if (position < line.Length && line[position] == ',')
                    position++;
                else if (position >= line.Length || line[position] != ']')
                {
                    error = "An array holds only strings on one line.";
                    return false;
                }
            }
        }

        var end = position;
        while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != '#')
            end++;

        var word = line[position..end];
        position = end;
        if (word == "true" || word == "false")
        {
            value = word == "true";
            return true;
        }

        if (long.TryParse(word.Replace("_", "", StringComparison.Ordinal), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            value = number;
            return true;
        }

        error = $"Value '{word}' is not a string, an integer, a boolean or an array of strings.";
        return false;
    }

    private static bool TryString(string line, ref int position, out string text, out string error)
    {
        text = "";
        error = "A string is not closed.";
        if (position >= line.Length || line[position] is not ('"' or '\''))
            return false;

        var quote = line[position++];
        var builder = new StringBuilder();
        while (position < line.Length)
        {
            var ch = line[position++];
            if (ch == quote)
            {
                text = builder.ToString();
                error = "";
                return true;
            }

            if (ch == '\\' && quote == '"')
            {
                if (position >= line.Length)
                    return false;

                var escaped = line[position++];
                switch (escaped)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case 'n': builder.Append('\n'); break;
                    case 't': builder.Append('\t'); break;
                    default:
                        error = $"Escape '\\{escaped}' is not supported.";
                        return false;
                }

                continue;
            }

            builder.Append(ch);
        }

        return false;
    }

    private static bool AtEnd(string line, int position)
    {
        position = SkipSpace(line, position);
        return position >= line.Length || line[position] == '#';
    }

    private static int SkipSpace(string line, int position)
    {
        while (position < line.Length && (line[position] == ' ' || line[position] == '\t'))
            position++;

        return position;
    }

    private static bool IsBareKey(string key)
    {
        if (key.Length == 0)
            return false;

        foreach (var ch in key)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '_' && ch != '-')
                return false;
        }

        return true;
    }
}

internal sealed class ManifestTable(string name, int line)
{
    public string Name { get; } = name;

    public int Line { get; } = line;

    public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);
}

internal sealed record ManifestTables(IReadOnlyList<ManifestTable> Tables, string? Error)
{
    public static ManifestTables Fail(int line, string message) =>
        new([], $"line {line.ToString(CultureInfo.InvariantCulture)}: {message}");
}
