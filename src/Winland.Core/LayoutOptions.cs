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

    /// <summary><c>general:gaps_workspaces</c>: the gap between Workspaces as one slides in, added to its travel.</summary>
    public int GapsWorkspaces { get; init; }

    public int BorderSize { get; init; }

    /// <summary>
    /// The border a Client's window properties give it, in logical pixels, or null to use <see cref="BorderSize"/>.
    /// The core sets one lookup for every Workspace, so options still compare equal.
    /// </summary>
    public Func<ClientId, int?>? ClientBorder { get; init; }

    /// <summary>The Monitor scale <see cref="ScaledBy"/> applied, which a Client's own border is multiplied by too.</summary>
    public double BorderScale { get; init; } = 1;

    /// <summary>The border around this Client, in pixels.</summary>
    internal int BorderFor(ClientId client) =>
        ClientBorder?.Invoke(client) is int own ? Scale(Math.Max(0, own), BorderScale) : BorderSize;

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

    /// <summary>
    /// The Monitor's strips to the left and right of its work area, such as a taskbar or an <c>addreserved</c> rule,
    /// in pixels. A centered master can ignore them with <c>master:center_ignores_reserved</c>.
    /// </summary>
    public int ReservedLeft { get; init; }

    public int ReservedRight { get; init; }

    /// <summary><c>layout:single_window_aspect_ratio</c>: the width to height a lone tiled Client is padded to. 0 0 is off.</summary>
    public Vec2 SingleWindowAspectRatio { get; init; }

    /// <summary><c>layout:single_window_aspect_ratio_tolerance</c>: padding smaller than this share of the work area is not added.</summary>
    public double SingleWindowAspectRatioTolerance { get; init; } = 0.1;

    /// <summary><c>misc:float_force_onscreen</c>: how far a floating Client is kept on screen when it moves, resizes or starts floating.</summary>
    public int FloatForceOnscreen { get; init; }

    /// <summary><c>misc:new_float_force_onscreen</c>: how far a Client that opens floating is kept on screen.</summary>
    public int NewFloatForceOnscreen { get; init; } = 2;

    public int ForceSplit { get; init; }

    public bool PreserveSplit { get; init; }

    public bool SmartSplit { get; init; }

    public bool PermanentDirectionOverride { get; init; }

    public double SplitWidthMultiplier { get; init; } = 1;

    public bool UseActiveForSplits { get; init; } = true;

    public double DefaultSplitRatio { get; init; } = 1;

    public int SplitBias { get; init; }

    public bool SmartResizing { get; init; } = true;

    public bool PreciseMouseMove { get; init; }

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
        ["general:gaps_workspaces"] = (options, value) => TryParseInt(value, out var between) ? options with { GapsWorkspaces = Clamp(between, 0, 100) } : options,
        ["general:border_size"] = (options, value) => TryParseInt(value, out var border) ? options with { BorderSize = Clamp(border, 0, 20) } : options,
        ["general:resize_on_border"] = (options, value) => TryParseBool(value, out var resize) ? options with { ResizeOnBorder = resize } : options,
        ["general:extend_border_grab_area"] = (options, value) => TryParseInt(value, out var extend) ? options with { ExtendBorderGrabArea = Clamp(extend, 0, 100) } : options,
        ["general:hover_icon_on_border"] = (options, value) => TryParseBool(value, out var hover) ? options with { HoverIconOnBorder = hover } : options,
        ["general:resize_corner"] = (options, value) => TryParseResizeCorner(value, out var corner) ? options with { ResizeCorner = corner } : options,
        ["general:float_gaps"] = (options, value) => TryParseGaps(value, out var floatGaps) ? options with { FloatGaps = floatGaps } : options,
        ["layout:single_window_aspect_ratio"] = (options, value) => ConfigValueSyntax.TryParseVec2(value, out var ratio) && ratio.X is >= 0 and <= 1000 && ratio.Y is >= 0 and <= 1000 ? options with { SingleWindowAspectRatio = ratio } : options,
        ["layout:single_window_aspect_ratio_tolerance"] = (options, value) => TryParseDouble(value, out var tolerance) ? options with { SingleWindowAspectRatioTolerance = Clamp(tolerance, 0, 1) } : options,
        ["misc:float_force_onscreen"] = (options, value) => TryParseInt(value, out var onscreen) ? options with { FloatForceOnscreen = Clamp(onscreen, 0, 2) } : options,
        ["misc:new_float_force_onscreen"] = (options, value) => TryParseInt(value, out var onscreen) ? options with { NewFloatForceOnscreen = Clamp(onscreen, 0, 2) } : options,
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
        ["dwindle:smart_resizing"] = (options, value) => TryParseBool(value, out var smartResizing) ? options with { SmartResizing = smartResizing } : options,
        ["dwindle:precise_mouse_move"] = (options, value) => TryParseBool(value, out var precise) ? options with { PreciseMouseMove = precise } : options,
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

    /// <summary>
    /// A tiled Client's frame, padded on two sides to <c>layout:single_window_aspect_ratio</c> when it is the only tiled
    /// Client, as Winland pads it. The padding is worked out from the whole work area and split evenly, and
    /// none is added when half of it would be less than the tolerance's share of the work area's height or width.
    /// </summary>
    internal Rectangle KeepAspect(Rectangle frame, Rectangle area, int tiled)
    {
        var ratio = SingleWindowAspectRatio;
        if (ratio.Y == 0 || tiled > 1 || area.Width <= 0 || area.Height <= 0)
            return frame;

        var requested = ratio.X / ratio.Y;
        var original = area.Width / (double)area.Height;
        var padX = 0.0;
        var padY = 0.0;
        if (requested > original)
        {
            var padding = area.Height - (area.Width / requested);
            if (padding / 2 > SingleWindowAspectRatioTolerance * area.Height)
                padY = padding;
        }
        else if (requested < original)
        {
            var padding = area.Width - (area.Height * requested);
            if (padding / 2 > SingleWindowAspectRatioTolerance * area.Width)
                padX = padding;
        }

        if (padX == 0 && padY == 0)
            return frame;

        return new Rectangle(
            (int)Math.Round(frame.X + (padX / 2)),
            (int)Math.Round(frame.Y + (padY / 2)),
            Math.Max(1, (int)Math.Round(frame.Width - padX)),
            Math.Max(1, (int)Math.Round(frame.Height - padY)));
    }

    /// <summary>How a floating Client on this area is kept on screen when it moves, resizes or starts floating.</summary>
    internal OnScreen FloatingOnScreen(Rectangle area) => new(FloatingWorkArea(area), BorderSize, FloatForceOnscreen);

    /// <summary>
    /// How a Client that opens floating on this area is kept on screen. Its border is left out, as in the pinned
    /// release, which keeps it on screen before the border is known.
    /// </summary>
    internal OnScreen NewFloatingOnScreen(Rectangle area) => new(FloatingWorkArea(area), 0, NewFloatForceOnscreen);

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

    /// <summary>Multiplies the gaps and borders, which the config gives in logical pixels, by a Monitor's scale.</summary>
    internal LayoutOptions ScaledBy(double scale)
    {
        if (scale is <= 0 or >= 1000 || !double.IsFinite(scale) || scale == 1)
            return this;

        return this with
        {
            GapsIn = Scale(GapsIn, scale),
            GapsOut = Scale(GapsOut, scale),
            GapsWorkspaces = Scale(GapsWorkspaces, scale),
            FloatGaps = Scale(FloatGaps, scale),
            BorderSize = Scale(BorderSize, scale),
            BorderScale = scale,
            ExtendBorderGrabArea = Scale(ExtendBorderGrabArea, scale),
            SnapWindowGap = Scale(SnapWindowGap, scale),
            SnapMonitorGap = Scale(SnapMonitorGap, scale),
        };
    }

    private static EdgeInsets Scale(EdgeInsets insets, double scale) =>
        new(Scale(insets.Top, scale), Scale(insets.Right, scale), Scale(insets.Bottom, scale), Scale(insets.Left, scale));

    private static int Scale(int value, double scale) =>
        (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);

    private static bool TryParseBool(string text, out bool value) =>
        ConfigValueSyntax.TryParseBool(text, out value);

    private static bool TryParseInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);
}
