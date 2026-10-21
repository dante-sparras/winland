using System.Globalization;

namespace Winland.Core;

public enum ScrollingDirection
{
    Right,
    Left,
    Down,
    Up,
}

/// <summary>How a focused column is brought into the viewport. <c>0</c> centers it. <c>1</c> fits it.</summary>
public enum ScrollingFit
{
    Center = 0,
    Fit = 1,
}

/// <summary>Options under <c>scrolling:</c>. Gaps and borders stay on <see cref="LayoutOptions"/>.</summary>
public sealed record ScrollingOptions
{
    public const double MinColumnWidth = 0.05;

    public const double MaxColumnWidth = 1;

    public bool FullscreenOnOneColumn { get; init; } = true;

    public double ColumnWidth { get; init; } = 0.5;

    public ScrollingFit FocusFitMethod { get; init; } = ScrollingFit.Fit;

    public bool FollowFocus { get; init; } = true;

    public double FollowMinVisible { get; init; } = 0.4;

    public IReadOnlyList<double> ExplicitColumnWidths { get; init; } = [0.333, 0.5, 0.667, 1.0];

    public ScrollingDirection Direction { get; init; } = ScrollingDirection.Right;

    public bool WrapFocus { get; init; } = true;

    public bool WrapSwapCol { get; init; } = true;

    public static ScrollingOptions Defaults { get; } = new();

    private static readonly Dictionary<string, int> FitNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["center"] = 0,
        ["fit"] = 1,
    };

    private static readonly Dictionary<string, ScrollingDirection> DirectionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["right"] = ScrollingDirection.Right,
        ["left"] = ScrollingDirection.Left,
        ["down"] = ScrollingDirection.Down,
        ["up"] = ScrollingDirection.Up,
    };

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
        Of("scrolling:fullscreen_on_one_column", OptionValue.Bool, o => o.FullscreenOnOneColumn, (o, v) => o with { FullscreenOnOneColumn = v }),
        Of("scrolling:column_width", OptionValue.Number(0.1, 1), o => o.ColumnWidth, (o, v) => o with { ColumnWidth = v }),
        Of("scrolling:focus_fit_method", OptionValue.Int(0, 1, FitNames), o => (int)o.FocusFitMethod, (o, v) => o with { FocusFitMethod = (ScrollingFit)v }),
        Of("scrolling:follow_focus", OptionValue.Bool, o => o.FollowFocus, (o, v) => o with { FollowFocus = v }),
        Of("scrolling:follow_min_visible", OptionValue.Number(0, 1), o => o.FollowMinVisible, (o, v) => o with { FollowMinVisible = v }),
        Of("scrolling:explicit_column_widths", Widths, o => o.ExplicitColumnWidths, (o, v) => o with { ExplicitColumnWidths = v }),
        Of("scrolling:direction", OptionValue.Named(DirectionNames), o => o.Direction, (o, v) => o with { Direction = v }),
        Of("scrolling:wrap_focus", OptionValue.Bool, o => o.WrapFocus, (o, v) => o with { WrapFocus = v }),
        Of("scrolling:wrap_swapcol", OptionValue.Bool, o => o.WrapSwapCol, (o, v) => o with { WrapSwapCol = v }),
    ]);

    public static ScrollingOptions From(ConfigDocument document) => Options.From(Defaults, document);

    private static Option<ScrollingOptions> Of<T>(string name, OptionValue<T> value, Func<ScrollingOptions, T> get, Func<ScrollingOptions, T, ScrollingOptions> set) =>
        Option<ScrollingOptions>.Of(name, value, get, set);
}
