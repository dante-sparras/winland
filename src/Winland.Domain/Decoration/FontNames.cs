namespace Winland.Domain;

/// <summary>Turns a font family the config names into one Windows has. The generic fontconfig names map to Windows fonts.</summary>
public static class FontNames
{
    public const string Default = "Segoe UI";

    private static readonly Dictionary<string, string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sans"] = Default,
        ["sans-serif"] = Default,
        ["sans serif"] = Default,
        ["system-ui"] = Default,
        ["serif"] = "Cambria",
        ["monospace"] = "Consolas",
        ["mono"] = "Consolas",
    };

    /// <summary>The Windows font for <paramref name="family"/>: a generic name's Windows font, the name itself, or Segoe UI for none.</summary>
    public static string Windows(string? family)
    {
        var name = family?.Trim() ?? "";
        if (name.Length == 0)
            return Default;

        return Generic.TryGetValue(name, out var mapped) ? mapped : name;
    }
}
