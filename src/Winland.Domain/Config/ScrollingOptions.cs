using System.Globalization;

namespace Winland.Domain;

/// <summary>Options under <c>scrolling:</c>. Gaps and borders stay on <see cref="LayoutOptions"/>.</summary>
public sealed record ScrollingOptions
{
    public const double MinColumnWidth = 0.05;

    public const double MaxColumnWidth = 1;

    public double ColumnWidth { get; init; } = 0.5;

    public IReadOnlyList<double> ExplicitColumnWidths { get; init; } = [0.333, 0.5, 0.667, 1.0];

    public static ScrollingOptions Defaults { get; } = new();

    // Widths split by commas, each clamped to a column's range. Every width has to read.
    private static readonly OptionValue<IReadOnlyList<double>> Widths = OptionValue.Of<IReadOnlyList<double>>(
        "str",
        (string text, out IReadOnlyList<double> widths) =>
        {
            var width = OptionValue.Number(MinColumnWidth, MaxColumnWidth);
            var parsed = new List<double>();
            widths = parsed;
            foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!width.Read(part, out var read))
                    return false;

                parsed.Add(read);
            }

            return parsed.Count > 0;
        },
        widths => OptionValue.Quote(string.Join(", ", widths.Select(width => width.ToString("0.###", CultureInfo.InvariantCulture)))));

    internal static OptionSet<ScrollingOptions> Options { get; } = new(
    [
        Of("scrolling:column_width", OptionValue.Number(0.1, 1), o => o.ColumnWidth, (o, v) => o with { ColumnWidth = v }),
        Of("scrolling:explicit_column_widths", Widths, o => o.ExplicitColumnWidths, (o, v) => o with { ExplicitColumnWidths = v }),
    ]);

    public static ScrollingOptions From(ConfigDocument document) => Options.From(Defaults, document);

    private static Option<ScrollingOptions> Of<T>(string name, OptionValue<T> value, Func<ScrollingOptions, T> get, Func<ScrollingOptions, T, ScrollingOptions> set) =>
        Option<ScrollingOptions>.Of(name, value, get, set);
}
