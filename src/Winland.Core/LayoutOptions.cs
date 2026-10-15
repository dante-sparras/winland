using System.Globalization;

namespace Winland;

public readonly record struct EdgeInsets(int Top, int Right, int Bottom, int Left)
{
    public static EdgeInsets Zero { get; } = new(0, 0, 0, 0);

    public static EdgeInsets Uniform(int value) => new(value, value, value, value);

    public Rectangle Apply(Rectangle area)
    {
        var width = Math.Max(0, area.Width - Left - Right);
        var height = Math.Max(0, area.Height - Top - Bottom);
        return new Rectangle(area.X + Left, area.Y + Top, width, height);
    }
}

public sealed record LayoutOptions
{
    public EdgeInsets GapsIn { get; init; } = EdgeInsets.Zero;

    public EdgeInsets GapsOut { get; init; } = EdgeInsets.Zero;

    public int BorderSize { get; init; }

    public bool ResizeOnBorder { get; init; }

    public int ExtendBorderGrabArea { get; init; } = 15;

    public bool HoverIconOnBorder { get; init; } = true;

    public int ResizeCorner { get; init; }

    public bool SnapEnabled { get; init; }

    public int SnapWindowGap { get; init; } = 10;

    public int SnapMonitorGap { get; init; } = 10;

    public bool SnapBorderOverlap { get; init; }

    public bool SnapRespectGaps { get; init; }

    public EdgeInsets FloatGaps { get; init; } = EdgeInsets.Zero;

    public int ForceSplit { get; init; }

    public bool PreserveSplit { get; init; }

    public bool SmartSplit { get; init; }

    public bool PermanentDirectionOverride { get; init; }

    public double SplitWidthMultiplier { get; init; } = 1;

    public bool UseActiveForSplits { get; init; } = true;

    public double DefaultSplitRatio { get; init; } = 1;

    public int SplitBias { get; init; }

    public static LayoutOptions Unconfigured { get; } = new();

    internal const string LayoutKeyword = "general:layout";

    // Values of general:layout that do not warn. Add a name here when that layout is selected.
    private static readonly HashSet<string> LayoutNames = new(StringComparer.Ordinal)
    {
        "dwindle",
        "master",
        "monocle",
        "scrolling",
    };

    internal static bool HonorsLayout(string value) => LayoutNames.Contains(value);

    // Adding an entry here applies that keyword and stops the config warning.
    private static readonly Dictionary<string, Func<LayoutOptions, string, LayoutOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["general:gaps_in"] = (options, value) => TryParseGaps(value, out var gaps) ? options with { GapsIn = gaps } : options,
        ["general:gaps_out"] = (options, value) => TryParseGaps(value, out var gaps) ? options with { GapsOut = gaps } : options,
        ["general:border_size"] = (options, value) => TryParseInt(value, out var border) ? options with { BorderSize = Clamp(border, 0, 20) } : options,
        ["general:resize_on_border"] = (options, value) => TryParseBool(value, out var resize) ? options with { ResizeOnBorder = resize } : options,
        ["general:extend_border_grab_area"] = (options, value) => TryParseInt(value, out var extend) ? options with { ExtendBorderGrabArea = Clamp(extend, 0, 100) } : options,
        ["general:hover_icon_on_border"] = (options, value) => TryParseBool(value, out var hover) ? options with { HoverIconOnBorder = hover } : options,
        ["general:resize_corner"] = (options, value) => TryParseResizeCorner(value, out var corner) ? options with { ResizeCorner = corner } : options,
        ["general:float_gaps"] = (options, value) => TryParseGaps(value, out var floatGaps) ? options with { FloatGaps = floatGaps } : options,
        ["general:snap:enabled"] = (options, value) => TryParseBool(value, out var snap) ? options with { SnapEnabled = snap } : options,
        ["general:snap:window_gap"] = (options, value) => TryParseInt(value, out var windowGap) ? options with { SnapWindowGap = Clamp(windowGap, 0, 100) } : options,
        ["general:snap:monitor_gap"] = (options, value) => TryParseInt(value, out var monitorGap) ? options with { SnapMonitorGap = Clamp(monitorGap, 0, 100) } : options,
        ["general:snap:border_overlap"] = (options, value) => TryParseBool(value, out var overlap) ? options with { SnapBorderOverlap = overlap } : options,
        ["general:snap:respect_gaps"] = (options, value) => TryParseBool(value, out var respect) ? options with { SnapRespectGaps = respect } : options,
        ["dwindle:force_split"] = (options, value) => TryParseForceSplit(value, out var force) ? options with { ForceSplit = force } : options,
        ["dwindle:preserve_split"] = (options, value) => TryParseBool(value, out var preserve) ? options with { PreserveSplit = preserve } : options,
        ["dwindle:smart_split"] = (options, value) => TryParseBool(value, out var smart) ? options with { SmartSplit = smart } : options,
        ["dwindle:permanent_direction_override"] = (options, value) => TryParseBool(value, out var permanent) ? options with { PermanentDirectionOverride = permanent } : options,
        ["dwindle:split_width_multiplier"] = (options, value) => TryParseDouble(value, out var multiplier) ? options with { SplitWidthMultiplier = Clamp(multiplier, 0.1, 3) } : options,
        ["dwindle:use_active_for_splits"] = (options, value) => TryParseBool(value, out var useActive) ? options with { UseActiveForSplits = useActive } : options,
        ["dwindle:default_split_ratio"] = (options, value) => TryParseDouble(value, out var ratio) ? options with { DefaultSplitRatio = Clamp(ratio, 0.1, 1.9) } : options,
        ["dwindle:split_bias"] = (options, value) => TryParseSplitBias(value, out var bias) ? options with { SplitBias = bias } : options,
    };

    internal static IReadOnlySet<string> Keywords { get; } = new HashSet<string>(Appliers.Keys, StringComparer.Ordinal)
    {
        LayoutKeyword,
    };

    public static LayoutOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented();
        foreach (var setting in document.Settings)
            options = Apply(options, setting);
        return options;
    }

    private static LayoutOptions Documented() => new()
    {
        GapsIn = EdgeInsets.Uniform(5),
        GapsOut = EdgeInsets.Uniform(20),
        BorderSize = 1,
    };

    private static LayoutOptions Apply(LayoutOptions options, Setting setting) =>
        Appliers.TryGetValue(setting.Keyword, out var apply) ? apply(options, setting.Value) : options;

    private static bool TryParseGaps(string text, out EdgeInsets gaps)
    {
        gaps = EdgeInsets.Zero;
        var parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        var count = Math.Min(parts.Length, 4);
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            if (!TryParseInt(parts[i], out values[i]))
                return false;
        }

        gaps = count switch
        {
            1 => EdgeInsets.Uniform(values[0]),
            2 => new EdgeInsets(values[0], values[1], values[0], values[1]),
            3 => new EdgeInsets(values[0], values[1], values[2], values[1]),
            _ => new EdgeInsets(values[0], values[1], values[2], values[3]),
        };
        return true;
    }

    private static readonly Dictionary<string, int> ForceSplitNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["follow_mouse"] = 0,
        ["left"] = 1,
        ["right"] = 2,
    };

    private static readonly Dictionary<string, int> SplitBiasNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["directional"] = 0,
        ["current"] = 1,
    };

    /// <summary>
    /// Gaps around floating Clients. A negative side means the floating gap was not set, so
    /// <see cref="GapsOut"/> is used instead.
    /// </summary>
    public EdgeInsets EffectiveFloatGaps =>
        FloatGaps.Top < 0 || FloatGaps.Right < 0 || FloatGaps.Bottom < 0 || FloatGaps.Left < 0
            ? GapsOut
            : FloatGaps;

    public Rectangle FloatingWorkArea(Rectangle area) => EffectiveFloatGaps.Apply(area);

    private static readonly Dictionary<string, int> ResizeCornerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disable"] = 0,
        ["top_left"] = 1,
        ["top_right"] = 2,
        ["bottom_right"] = 3,
        ["bottom_left"] = 4,
    };

    private static bool TryParseResizeCorner(string text, out int value)
    {
        if (!ConfigValueSyntax.TryParseNamedInt(text, ResizeCornerNames, out var parsed))
        {
            value = 0;
            return false;
        }

        value = Clamp(parsed, 0, 4);
        return true;
    }

    private static bool TryParseForceSplit(string text, out int value)
    {
        if (!ConfigValueSyntax.TryParseNamedInt(text, ForceSplitNames, out var parsed))
        {
            value = 0;
            return false;
        }

        value = Clamp(parsed, 0, 2);
        return true;
    }

    private static bool TryParseSplitBias(string text, out int value)
    {
        if (!ConfigValueSyntax.TryParseNamedInt(text, SplitBiasNames, out var parsed))
        {
            value = 0;
            return false;
        }

        value = Clamp(parsed, 0, 1);
        return true;
    }

    internal static bool TryOverrideDwindle(LayoutOptions options, string key, string value, out LayoutOptions updated)
    {
        ArgumentNullException.ThrowIfNull(options);
        updated = options;
        if (!Appliers.TryGetValue("dwindle:" + key, out var apply))
            return false;

        var next = apply(options, value);
        if (ReferenceEquals(next, options))
            return false;

        updated = next;
        return true;
    }

    private static bool TryParseBool(string text, out bool value) =>
        ConfigValueSyntax.TryParseBool(text, out value);

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);
}
