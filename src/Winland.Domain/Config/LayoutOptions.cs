using System.Globalization;

namespace Winland.Domain;

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

    /// <summary><c>general:layout</c>: the Layout a Workspace uses when no workspace rule names one.</summary>
    public string Layout { get; init; } = "dwindle";

    /// <summary><c>dwindle:special_scale_factor</c>: read and kept. A special workspace is not scaled on Windows.</summary>
    public double SpecialScaleFactor { get; init; } = 1;

    /// <summary>Before the config is read: no gaps and no border.</summary>
    public static LayoutOptions Unconfigured { get; } = new();

    /// <summary>The options a config that sets none of them gives.</summary>
    public static LayoutOptions Documented { get; } = new()
    {
        GapsIn = EdgeInsets.Uniform(4),
        GapsOut = EdgeInsets.Uniform(8),
        BorderSize = 2,
    };

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

    private static readonly Dictionary<string, int> ResizeCornerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disable"] = 0,
        ["top_left"] = 1,
        ["top_right"] = 2,
        ["bottom_right"] = 3,
        ["bottom_left"] = 4,
    };

    internal static OptionSet<LayoutOptions> Options { get; } = new(
    [
        Of("general:layout", OptionValue.OneOf([.. LayoutFactory.Names]), o => o.Layout, (o, v) => o with { Layout = v }),
        Of("general:gaps_in", OptionValue.Gaps, o => o.GapsIn, (o, v) => o with { GapsIn = v }),
        Of("general:gaps_out", OptionValue.Gaps, o => o.GapsOut, (o, v) => o with { GapsOut = v }),
        Of("general:gaps_workspaces", OptionValue.Int(0, 100), o => o.GapsWorkspaces, (o, v) => o with { GapsWorkspaces = v }),
        Of("general:border_size", OptionValue.Int(0, 20), o => o.BorderSize, (o, v) => o with { BorderSize = v }),
        Of("general:resize_on_border", OptionValue.Bool, o => o.ResizeOnBorder, (o, v) => o with { ResizeOnBorder = v }),
        Of("general:extend_border_grab_area", OptionValue.Int(0, 100), o => o.ExtendBorderGrabArea, (o, v) => o with { ExtendBorderGrabArea = v }),
        Of("general:hover_icon_on_border", OptionValue.Bool, o => o.HoverIconOnBorder, (o, v) => o with { HoverIconOnBorder = v }),
        Of("general:resize_corner", OptionValue.Int(0, 4, ResizeCornerNames), o => o.ResizeCorner, (o, v) => o with { ResizeCorner = v }),
        Of("general:float_gaps", OptionValue.Gaps, o => o.FloatGaps, (o, v) => o with { FloatGaps = v }),
        Of("layout:single_window_aspect_ratio", OptionValue.Pair(0, 1000), o => o.SingleWindowAspectRatio, (o, v) => o with { SingleWindowAspectRatio = v }),
        Of("layout:single_window_aspect_ratio_tolerance", OptionValue.Number(0, 1), o => o.SingleWindowAspectRatioTolerance, (o, v) => o with { SingleWindowAspectRatioTolerance = v }),
        Of("misc:float_force_onscreen", OptionValue.Int(0, 2), o => o.FloatForceOnscreen, (o, v) => o with { FloatForceOnscreen = v }),
        Of("misc:new_float_force_onscreen", OptionValue.Int(0, 2), o => o.NewFloatForceOnscreen, (o, v) => o with { NewFloatForceOnscreen = v }),
        Of("general:snap:enabled", OptionValue.Bool, o => o.SnapEnabled, (o, v) => o with { SnapEnabled = v }),
        Of("general:snap:window_gap", OptionValue.Int(0, 100), o => o.SnapWindowGap, (o, v) => o with { SnapWindowGap = v }),
        Of("general:snap:monitor_gap", OptionValue.Int(0, 100), o => o.SnapMonitorGap, (o, v) => o with { SnapMonitorGap = v }),
        Of("general:snap:border_overlap", OptionValue.Bool, o => o.SnapBorderOverlap, (o, v) => o with { SnapBorderOverlap = v }),
        Of("general:snap:respect_gaps", OptionValue.Bool, o => o.SnapRespectGaps, (o, v) => o with { SnapRespectGaps = v }),
        Of("dwindle:force_split", OptionValue.Int(0, 2, ForceSplitNames), o => o.ForceSplit, (o, v) => o with { ForceSplit = v }),
        Of("dwindle:preserve_split", OptionValue.Bool, o => o.PreserveSplit, (o, v) => o with { PreserveSplit = v }),
        Of("dwindle:smart_split", OptionValue.Bool, o => o.SmartSplit, (o, v) => o with { SmartSplit = v }),
        Of("dwindle:permanent_direction_override", OptionValue.Bool, o => o.PermanentDirectionOverride, (o, v) => o with { PermanentDirectionOverride = v }),
        Of("dwindle:special_scale_factor", OptionValue.Number(0, 1), o => o.SpecialScaleFactor, (o, v) => o with { SpecialScaleFactor = v }),
        Of("dwindle:split_width_multiplier", OptionValue.Number(0.1, 3), o => o.SplitWidthMultiplier, (o, v) => o with { SplitWidthMultiplier = v }),
        Of("dwindle:use_active_for_splits", OptionValue.Bool, o => o.UseActiveForSplits, (o, v) => o with { UseActiveForSplits = v }),
        Of("dwindle:default_split_ratio", OptionValue.Number(0.1, 1.9), o => o.DefaultSplitRatio, (o, v) => o with { DefaultSplitRatio = v }),
        Of("dwindle:split_bias", OptionValue.Int(0, 1, SplitBiasNames), o => o.SplitBias, (o, v) => o with { SplitBias = v }),
        Of("dwindle:smart_resizing", OptionValue.Bool, o => o.SmartResizing, (o, v) => o with { SmartResizing = v }),
        Of("dwindle:precise_mouse_move", OptionValue.Bool, o => o.PreciseMouseMove, (o, v) => o with { PreciseMouseMove = v }),
    ]);

    public static LayoutOptions From(ConfigDocument document) => Options.From(Documented, document);

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

    // A workspace rule's layoutopt sets a dwindle Option for that Workspace.
    internal static bool TryOverrideDwindle(LayoutOptions options, string key, string value, out LayoutOptions updated)
    {
        ArgumentNullException.ThrowIfNull(options);
        updated = Options.Apply(options, "dwindle:" + key, value) ?? options;
        return !ReferenceEquals(updated, options);
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

    private static Option<LayoutOptions> Of<T>(string name, OptionValue<T> value, Func<LayoutOptions, T> get, Func<LayoutOptions, T, LayoutOptions> set) =>
        Option<LayoutOptions>.Of(name, value, get, set);
}
