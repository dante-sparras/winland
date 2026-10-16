using System.Globalization;

namespace Winland;

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

    public static ScrollingOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Defaults;
        foreach (var setting in document.Settings)
            options = Apply(options, setting);
        return options;
    }

    private static readonly Dictionary<string, Func<ScrollingOptions, string, ScrollingOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["scrolling:fullscreen_on_one_column"] = (options, value) =>
            TryBool(value, out var enabled) ? options with { FullscreenOnOneColumn = enabled } : options,
        ["scrolling:column_width"] = (options, value) =>
            TryDouble(value, out var width) ? options with { ColumnWidth = Clamp(width, 0.1, 1) } : options,
        ["scrolling:focus_fit_method"] = (options, value) =>
            TryFit(value, out var fit) ? options with { FocusFitMethod = fit } : options,
        ["scrolling:follow_focus"] = (options, value) =>
            TryBool(value, out var follow) ? options with { FollowFocus = follow } : options,
        ["scrolling:follow_min_visible"] = (options, value) =>
            TryDouble(value, out var fraction) ? options with { FollowMinVisible = Clamp(fraction, 0, 1) } : options,
        ["scrolling:explicit_column_widths"] = (options, value) =>
            TryWidths(value, out var widths) ? options with { ExplicitColumnWidths = widths } : options,
        ["scrolling:direction"] = (options, value) => options with { Direction = ParseDirection(value) },
        ["scrolling:wrap_focus"] = (options, value) =>
            TryBool(value, out var wrap) ? options with { WrapFocus = wrap } : options,
        ["scrolling:wrap_swapcol"] = (options, value) =>
            TryBool(value, out var wrap) ? options with { WrapSwapCol = wrap } : options,
    };

    internal static IReadOnlySet<string> Keywords { get; } = new HashSet<string>(Appliers.Keys, StringComparer.Ordinal);

    private static ScrollingOptions Apply(ScrollingOptions options, Setting setting) =>
        Appliers.TryGetValue(setting.Keyword, out var apply) ? apply(options, setting.Value) : options;

    private static readonly Dictionary<string, ScrollingFit> FitNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["center"] = ScrollingFit.Center,
        ["fit"] = ScrollingFit.Fit,
    };

    private static bool TryFit(string text, out ScrollingFit fit)
    {
        if (FitNames.TryGetValue(text, out fit))
            return true;

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            fit = ScrollingFit.Fit;
            return false;
        }

        fit = (ScrollingFit)Clamp(parsed, 0, 1);
        return true;
    }

    private static ScrollingDirection ParseDirection(string text)
    {
        if (text.Equals("left", StringComparison.OrdinalIgnoreCase))
            return ScrollingDirection.Left;
        if (text.Equals("down", StringComparison.OrdinalIgnoreCase))
            return ScrollingDirection.Down;
        if (text.Equals("up", StringComparison.OrdinalIgnoreCase))
            return ScrollingDirection.Up;
        return ScrollingDirection.Right;
    }

    private static bool TryWidths(string text, out IReadOnlyList<double> widths)
    {
        var parsed = new List<double>();
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryDouble(part, out var width))
                parsed.Add(Clamp(width, MinColumnWidth, MaxColumnWidth));
        }

        if (parsed.Count == 0)
        {
            widths = Defaults.ExplicitColumnWidths;
            return text.Trim().Length > 0;
        }

        widths = parsed;
        return true;
    }

    private static bool TryBool(string text, out bool value) =>
        ConfigValueSyntax.TryParseBool(text, out value);

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
}
