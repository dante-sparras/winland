using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// The one config notification a load shows: the count of config errors, then the first error with its file and
/// line. The very first start shows <see cref="Welcome"/> instead when there are no errors. A later load replaces
/// it. <see cref="Generation"/> changes even when the text does not, so a dismissed notification is shown again.
/// </summary>
public sealed record ConfigNotice(string Title, string Text, string? File, int Generation, bool Informational = false)
{
    /// <summary>
    /// The notification for the first start, which wrote the default config at <paramref name="configPath"/>.
    /// It says what Winland is doing and what the menu offers, and a click opens the config.
    /// </summary>
    public static ConfigNotice Welcome(string? configPath, int generation) => new(
        "Winland",
        "Winland is tiling your windows. Right-click its icon to edit the config or quit.",
        string.IsNullOrEmpty(configPath) ? null : configPath,
        generation,
        Informational: true);

    /// <summary>
    /// The notification for <paramref name="warnings"/>, or null when there are none. <paramref name="configPath"/> names the file a warning with no source was read from.
    /// </summary>
    public static ConfigNotice? For(IReadOnlyList<ConfigWarning> warnings, string? configPath, int generation)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        ConfigWarning? first = null;
        var count = 0;
        foreach (var warning in warnings)
        {
            count++;
            first ??= warning;
        }

        if (count == 0 || first is null)
            return null;

        var file = string.IsNullOrEmpty(first.Source) ? configPath : first.Source;
        if (string.IsNullOrEmpty(file))
            file = null;

        return new ConfigNotice(
            string.Create(CultureInfo.InvariantCulture, $"Winland: {count} config errors"),
            Body(first, file),
            file,
            generation);
    }

    private static string Body(ConfigWarning warning, string? file)
    {
        if (!string.IsNullOrEmpty(file) && warning.Line > 0)
            return string.Create(CultureInfo.InvariantCulture, $"{file}:{warning.Line}: {warning.Message}");

        if (warning.Line > 0)
            return string.Create(CultureInfo.InvariantCulture, $"line {warning.Line}: {warning.Message}");

        if (!string.IsNullOrEmpty(file))
            return string.Create(CultureInfo.InvariantCulture, $"{file}: {warning.Message}");

        return warning.Message;
    }
}
