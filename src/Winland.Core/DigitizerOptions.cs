using System.Globalization;

namespace Winland;

/// <summary>
/// <c>input:tablet</c>: how every pen is mapped. Windows has no call that changes which display a pen maps to, so
/// these values move the cursor the mouse hook sees.
/// </summary>
public sealed record TabletSettings
{
    /// <summary><c>input:tablet:enabled</c>. False swallows the cursor Windows makes from the pen.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary><c>input:tablet:transform</c>, 0 to 6. 0 leaves the position as Windows mapped it.</summary>
    public int Transform { get; init; }

    /// <summary><c>input:tablet:output</c>. Empty and <c>[[EMPTY]]</c> map the pen across every monitor.</summary>
    public string Output { get; init; } = "";

    /// <summary><c>input:tablet:absolute_region_position</c>. Used only when <see cref="Output"/> names no monitor.</summary>
    public bool AbsoluteRegion { get; init; }

    /// <summary><c>input:tablet:region_position</c>, in physical pixels of the virtual screen.</summary>
    public Vec2 RegionPosition { get; init; }

    /// <summary><c>input:tablet:region_size</c>, in physical pixels. Both numbers must be above 0 to replace the area.</summary>
    public Vec2 RegionSize { get; init; }

    /// <summary><c>input:tablet:active_area_position</c>, in millimetres of the tablet.</summary>
    public Vec2 ActiveAreaPosition { get; init; }

    /// <summary><c>input:tablet:active_area_size</c>, in millimetres. Both numbers must be above 0, and Windows must report a size.</summary>
    public Vec2 ActiveAreaSize { get; init; }

    /// <summary><c>input:tablet:relative_input</c>. The cursor moves by the change in the mapped pixel, not by a device unit.</summary>
    public bool Relative { get; init; }

    /// <summary><c>input:tablet:left_handed</c>. A half turn after <see cref="Transform"/>.</summary>
    public bool LeftHanded { get; init; }
}

/// <summary><c>input:touchdevice</c>: how every touch screen is mapped, for the cursor and for a workspace swipe.</summary>
public sealed record TouchSettings
{
    /// <summary><c>input:touchdevice:enabled</c>. False swallows the cursor and ignores the swipe. The device stays enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary><c>input:touchdevice:transform</c>, 0 to 6, applied after Windows has turned the touch screen with its display.</summary>
    public int Transform { get; init; }

    /// <summary>
    /// <c>input:touchdevice:output</c>. Empty, <c>[[EMPTY]]</c> and <c>[[Auto]]</c> keep the display Windows maps the touch screen to.
    /// <c>[[Auto]]</c> is the pinned release's unfinished autodetect, which on Windows is that display.
    /// </summary>
    public string Output { get; init; } = "";
}

/// <summary>The pen and touch screen settings, read from the config. They apply to every such device.</summary>
public sealed record DigitizerOptions
{
    public TabletSettings Tablet { get; init; } = new();

    public TouchSettings Touch { get; init; } = new();

    public static DigitizerOptions Documented { get; } = new();

    private static readonly Dictionary<string, Func<DigitizerOptions, string, DigitizerOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["input:tablet:enabled"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { Tablet = options.Tablet with { Enabled = enabled } } : options,
        ["input:tablet:transform"] = (options, value) => TryTransform(value, out var transform) ? options with { Tablet = options.Tablet with { Transform = transform } } : options,
        ["input:tablet:output"] = (options, value) => options with { Tablet = options.Tablet with { Output = value.Trim() } },
        ["input:tablet:absolute_region_position"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var absolute) ? options with { Tablet = options.Tablet with { AbsoluteRegion = absolute } } : options,
        ["input:tablet:region_position"] = (options, value) => TryVec(value, -20000, 20000, out var position) ? options with { Tablet = options.Tablet with { RegionPosition = position } } : options,
        ["input:tablet:region_size"] = (options, value) => TryVec(value, -100, 4000, out var size) ? options with { Tablet = options.Tablet with { RegionSize = size } } : options,
        ["input:tablet:active_area_position"] = (options, value) => TryVec(value, 0, 500, out var position) ? options with { Tablet = options.Tablet with { ActiveAreaPosition = position } } : options,
        ["input:tablet:active_area_size"] = (options, value) => TryVec(value, 0, 500, out var size) ? options with { Tablet = options.Tablet with { ActiveAreaSize = size } } : options,
        ["input:tablet:relative_input"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var relative) ? options with { Tablet = options.Tablet with { Relative = relative } } : options,
        ["input:tablet:left_handed"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var left) ? options with { Tablet = options.Tablet with { LeftHanded = left } } : options,
        ["input:touchdevice:enabled"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var enabled) ? options with { Touch = options.Touch with { Enabled = enabled } } : options,
        ["input:touchdevice:output"] = (options, value) => options with { Touch = options.Touch with { Output = value.Trim() } },
        ["input:touchdevice:transform"] = (options, value) => TryTransform(value, out var transform) ? options with { Touch = options.Touch with { Transform = transform } } : options,
    };

    internal static IReadOnlySet<string> Keywords { get; } = Appliers.Keys.ToHashSet(StringComparer.Ordinal);

    public static DigitizerOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented;
        foreach (var setting in document.Settings)
            options = Appliers.TryGetValue(setting.Keyword, out var apply) ? apply(options, setting.Value) : options;
        return options;
    }

    // 0 to 6, as the pinned release clamps a transform. A value that is not an integer leaves the previous one.
    private static bool TryTransform(string text, out int transform)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out transform))
            return false;

        transform = Math.Clamp(transform, 0, 6);
        return true;
    }

    private static bool TryVec(string text, double min, double max, out Vec2 value)
    {
        if (!ConfigValueSyntax.TryParseVec2(text.Trim(), out value) || !double.IsFinite(value.X) || !double.IsFinite(value.Y))
            return false;

        value = new Vec2(Math.Clamp(value.X, min, max), Math.Clamp(value.Y, min, max));
        return true;
    }
}
