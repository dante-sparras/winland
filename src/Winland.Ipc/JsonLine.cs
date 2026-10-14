using System.Text;
using System.Text.Json;

namespace Winland.Ipc;

/// <summary>
/// A JSON object written as one line. The writer does not reflect over a type: native <c>winland.exe</c> links this assembly,
/// and a reflected serializer does not.
/// </summary>
public static class JsonLine
{
    /// <summary>Writes one object.</summary>
    /// <param name="fields">Names and values. A value is a string, a bool, a number, null, a string array, a long array, or a key array.</param>
    /// <returns>The object, with no newline.</returns>
    public static string Object(params (string Name, object? Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in fields)
            {
                writer.WritePropertyName(name);
                Write(writer, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads one object. The caller disposes the document.</summary>
    /// <param name="line">One JSON object.</param>
    /// <param name="document">The document, when the answer is true.</param>
    /// <returns>False when <paramref name="line"/> is not a JSON object.</returns>
    public static bool TryParse(string? line, out JsonDocument document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            var parsed = JsonDocument.Parse(line);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                return false;
            }

            document = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>The <c>kind</c> string, or empty when the line has none.</summary>
    /// <param name="root">An object.</param>
    /// <returns>The kind.</returns>
    public static string Kind(JsonElement root) => Text(root, "kind");

    /// <summary>A string property, or empty when it is missing or not a string.</summary>
    /// <param name="root">An object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The text.</returns>
    public static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    /// <summary>A bool property. Missing is false.</summary>
    /// <param name="root">An object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The bool.</returns>
    public static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>A string array property. Missing is empty.</summary>
    /// <param name="root">An object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The strings. An element that is not a string is skipped.</returns>
    public static string[] Texts(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                list.Add(item.GetString() ?? "");
            }
        }

        return [.. list];
    }

    private static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case string[] texts:
                writer.WriteStartArray();
                foreach (var text in texts)
                {
                    writer.WriteStringValue(text);
                }

                writer.WriteEndArray();
                break;
            case long[] numbers:
                writer.WriteStartArray();
                foreach (var number in numbers)
                {
                    writer.WriteNumberValue(number);
                }

                writer.WriteEndArray();
                break;
            case ClaimedKey[] keys:
                writer.WriteStartArray();
                foreach (var key in keys)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("vk", key.Vk);
                    writer.WriteBoolean("super", key.Super);
                    writer.WriteBoolean("ctrl", key.Ctrl);
                    writer.WriteBoolean("alt", key.Alt);
                    writer.WriteNumber("id", key.Id);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
            default:
                throw new ArgumentException("A JSON line holds a string, a bool, a number, null, or a list of those.", nameof(value));
        }
    }
}
