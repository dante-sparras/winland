using System.Text.Json;

namespace Winland.Core;

/// <summary>
/// The tiling settings the window manager reads from <c>settings.json</c>, and the two toggles a chord writes back.
/// The shell owns the rest of the file. A write changes only those two toggles and leaves every other key as it was.
/// </summary>
public sealed class TileSettings : Shell.ITileSettings
{
    /// <summary>The face the bar draws with while <c>settings.json</c> names none. The shell lays its own face over the desktop reply.</summary>
    public const string DefaultFont = "Segoe UI Variable";

    /// <inheritdoc />
    public int SpacingPixels { get; set; } = 8;

    /// <inheritdoc />
    public bool SpacingEnabled { get; set; }

    /// <inheritdoc />
    public bool SquareTileEnabled { get; set; }

    /// <inheritdoc />
    public bool BarVisible { get; set; } = true;

    /// <inheritdoc />
    public bool IdleLockEnabled { get; set; } = true;

    /// <inheritdoc />
    public string Browser { get; set; } = "";

    /// <inheritdoc />
    public string FontFace { get; set; } = DefaultFont;

    /// <summary>True when windows that run as administrator are tiled. The elevated helper reads this. Off until the file says otherwise.</summary>
    public bool AdministratorWindows { get; set; }

    /// <summary>Copies the tiling fields from <paramref name="source"/> onto this instance.</summary>
    /// <param name="source">The settings just read from disk.</param>
    public void CopyFrom(TileSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        SpacingPixels = source.SpacingPixels;
        SpacingEnabled = source.SpacingEnabled;
        SquareTileEnabled = source.SquareTileEnabled;
        BarVisible = source.BarVisible;
        IdleLockEnabled = source.IdleLockEnabled;
        Browser = source.Browser;
        FontFace = source.FontFace;
        AdministratorWindows = source.AdministratorWindows;
    }
}

/// <summary>Reads and writes the tiling keys of <c>settings.json</c> without taking the rest of the shell's settings.</summary>
public static class TileSettingsFile
{
    /// <summary>The file's name in the config folder.</summary>
    public const string FileName = "settings.json";

    /// <summary>
    /// Reads the tiling keys from <paramref name="json"/>. A missing key keeps its default.
    /// A negative gap becomes 0. A font that is blank is <see cref="TileSettings.DefaultFont"/>.
    /// </summary>
    /// <param name="json">The file's text.</param>
    /// <returns>The tiling settings.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static TileSettings Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var settings = new TileSettings();
        if (json.Trim().Length == 0)
        {
            return settings;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return settings;
        }

        var root = document.RootElement;
        if (root.TryGetProperty("spacingPixels", out var pixels) && pixels.TryGetInt32(out var gap))
        {
            settings.SpacingPixels = Math.Max(0, gap);
        }

        settings.SpacingEnabled = Flag(root, "spacingEnabled", settings.SpacingEnabled);
        settings.SquareTileEnabled = Flag(root, "squareTileEnabled", settings.SquareTileEnabled);
        settings.BarVisible = Flag(root, "barVisible", settings.BarVisible);
        settings.IdleLockEnabled = Flag(root, "idleLockEnabled", settings.IdleLockEnabled);
        settings.AdministratorWindows = Flag(root, "administratorWindows", settings.AdministratorWindows);
        settings.Browser = Text(root, "browser") ?? "";
        var font = Text(root, "font");
        settings.FontFace = string.IsNullOrWhiteSpace(font) ? TileSettings.DefaultFont : font;
        return settings;
    }

    /// <summary>
    /// Loads <paramref name="path"/>, or the defaults when the file is missing or does not parse.
    /// A file that does not parse is left where it is: the shell is the one that copies it aside.
    /// </summary>
    /// <param name="path">The settings file.</param>
    /// <returns>The tiling settings, or the defaults.</returns>
    public static TileSettings LoadOrDefaults(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (!File.Exists(path))
            {
                return new TileSettings();
            }

            var read = ConfigJson.Read(path);
            foreach (var note in read.Notes)
            {
                Diagnostics.Log.Warning("Winland", note);
            }

            return Read(read.Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log.Warning("Winland", "settings.json was not read; the tiling defaults are in use", ex);
            return new TileSettings();
        }
    }

    /// <summary>
    /// Writes <paramref name="spacing"/> and <paramref name="square"/> into <paramref name="path"/> and leaves every other key.
    /// A missing file becomes an object that holds only those two. The folder is created when it is missing.
    /// </summary>
    /// <param name="path">The settings file.</param>
    /// <param name="spacing">Whether gutters are drawn between tiles.</param>
    /// <param name="square">Whether a workspace's one tiled window is drawn as a square.</param>
    public static void WriteToggles(string path, bool spacing, bool square)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        AtomicFile.Write(path, WithToggles(text, spacing, square));
    }

    /// <summary>The text of <paramref name="json"/> with the two tiling toggles set, and every other key left as it was.</summary>
    /// <param name="json">The file's text. Empty, and text that is not an object, become an object that holds only the toggles.</param>
    /// <param name="spacing">Whether gutters are drawn between tiles.</param>
    /// <param name="square">Whether a workspace's one tiled window is drawn as a square.</param>
    /// <returns>The whole new file.</returns>
    public static string WithToggles(string json, bool spacing, bool square)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            var sawSpacing = false;
            var sawSquare = false;
            if (TryObject(json, out var root))
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (property.NameEquals("spacingEnabled"))
                    {
                        writer.WriteBoolean("spacingEnabled", spacing);
                        sawSpacing = true;
                        continue;
                    }

                    if (property.NameEquals("squareTileEnabled"))
                    {
                        writer.WriteBoolean("squareTileEnabled", square);
                        sawSquare = true;
                        continue;
                    }

                    property.WriteTo(writer);
                }
            }

            if (!sawSpacing)
            {
                writer.WriteBoolean("spacingEnabled", spacing);
            }

            if (!sawSquare)
            {
                writer.WriteBoolean("squareTileEnabled", square);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool TryObject(string json, out JsonElement root)
    {
        root = default;
        if (json.Trim().Length == 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool Flag(JsonElement root, string name, bool fallback)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
