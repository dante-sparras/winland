namespace Winland;

/// <summary>The <c>group:*</c> and <c>group:groupbar:*</c> options, with the defaults of Winland.</summary>
public sealed record GroupOptions
{
    public static GroupOptions Documented { get; } = new();

    public bool AutoGroup { get; init; } = true;

    public bool InsertAfterCurrent { get; init; } = true;

    public bool FocusRemovedWindow { get; init; } = true;

    public bool GroupOnMoveToWorkspace { get; init; }

    public bool IgnoreGroupLock { get; init; }

    public bool MoveFocusCyclesGroupFirst { get; init; }

    /// <summary><c>group:drag_into_group</c>: 0 never, 1 onto a member or its tab bar, 2 only onto the tab bar.</summary>
    public int DragIntoGroup { get; init; } = 1;

    public bool MergeGroupsOnDrag { get; init; } = true;

    public bool MergeGroupsOnGroupbar { get; init; } = true;

    public bool MergeFloatedIntoTiledOnGroupbar { get; init; }

    public BorderColor ActiveBorder { get; init; } = new(0x66, 0xFF, 0xFF, 0x00);

    public BorderColor InactiveBorder { get; init; } = new(0x66, 0x77, 0x77, 0x00);

    public BorderColor LockedActiveBorder { get; init; } = new(0x66, 0x77, 0x55, 0x00);

    public BorderColor LockedInactiveBorder { get; init; } = new(0x66, 0xFF, 0x55, 0x00);

    public BorderColor NoGroupBorder { get; init; } = new(0xFF, 0xFF, 0xFF, 0xAA);

    public BorderColor NoGroupActiveBorder { get; init; } = new(0xFF, 0xFF, 0x00, 0xFF);

    public GroupBarStyle Bar { get; init; } = GroupBarStyle.Documented;

    private static readonly Dictionary<string, int> DragIntoGroupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disabled"] = 0,
        ["enabled"] = 1,
        ["only_into_groupbar"] = 2,
    };

    // A font weight is a name or a number from 100 to 1000.
    private static readonly OptionValue<int> Weight = OptionValue.Int(100, 1000, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["thin"] = 100,
        ["ultralight"] = 200,
        ["light"] = 300,
        ["book"] = 380,
        ["normal"] = 400,
        ["medium"] = 500,
        ["semibold"] = 600,
        ["bold"] = 700,
        ["ultrabold"] = 800,
        ["heavy"] = 900,
        ["ultraheavy"] = 1000,
    });

    internal static OptionSet<GroupOptions> Options { get; } = new(
    [
        Of("group:auto_group", OptionValue.Bool, o => o.AutoGroup, (o, v) => o with { AutoGroup = v }),
        Of("group:insert_after_current", OptionValue.Bool, o => o.InsertAfterCurrent, (o, v) => o with { InsertAfterCurrent = v }),
        Of("group:focus_removed_window", OptionValue.Bool, o => o.FocusRemovedWindow, (o, v) => o with { FocusRemovedWindow = v }),
        Of("group:group_on_movetoworkspace", OptionValue.Bool, o => o.GroupOnMoveToWorkspace, (o, v) => o with { GroupOnMoveToWorkspace = v }),
        Of("binds:ignore_group_lock", OptionValue.Bool, o => o.IgnoreGroupLock, (o, v) => o with { IgnoreGroupLock = v }),
        Of("binds:movefocus_cycles_groupfirst", OptionValue.Bool, o => o.MoveFocusCyclesGroupFirst, (o, v) => o with { MoveFocusCyclesGroupFirst = v }),
        Of("group:drag_into_group", OptionValue.Int(0, 2, DragIntoGroupNames), o => o.DragIntoGroup, (o, v) => o with { DragIntoGroup = v }),
        Of("group:merge_groups_on_drag", OptionValue.Bool, o => o.MergeGroupsOnDrag, (o, v) => o with { MergeGroupsOnDrag = v }),
        Of("group:merge_groups_on_groupbar", OptionValue.Bool, o => o.MergeGroupsOnGroupbar, (o, v) => o with { MergeGroupsOnGroupbar = v }),
        Of("group:merge_floated_into_tiled_on_groupbar", OptionValue.Bool, o => o.MergeFloatedIntoTiledOnGroupbar, (o, v) => o with { MergeFloatedIntoTiledOnGroupbar = v }),
        Of("group:col.border_active", OptionValue.Color, o => o.ActiveBorder, (o, v) => o with { ActiveBorder = v }),
        Of("group:col.border_inactive", OptionValue.Color, o => o.InactiveBorder, (o, v) => o with { InactiveBorder = v }),
        Of("group:col.border_locked_active", OptionValue.Color, o => o.LockedActiveBorder, (o, v) => o with { LockedActiveBorder = v }),
        Of("group:col.border_locked_inactive", OptionValue.Color, o => o.LockedInactiveBorder, (o, v) => o with { LockedInactiveBorder = v }),
        Of("general:col.nogroup_border", OptionValue.Color, o => o.NoGroupBorder, (o, v) => o with { NoGroupBorder = v }),
        Of("general:col.nogroup_border_active", OptionValue.Color, o => o.NoGroupActiveBorder, (o, v) => o with { NoGroupActiveBorder = v }),
        OfBar("enabled", OptionValue.Bool, b => b.Enabled, (b, v) => b with { Enabled = v }),
        OfBar("disable_when_only", OptionValue.Bool, b => b.DisableWhenOnly, (b, v) => b with { DisableWhenOnly = v }),
        OfBar("render_titles", OptionValue.Bool, b => b.RenderTitles, (b, v) => b with { RenderTitles = v }),
        OfBar("scrolling", OptionValue.Bool, b => b.Scrolling, (b, v) => b with { Scrolling = v }),
        OfBar("middle_click_close", OptionValue.Bool, b => b.MiddleClickClose, (b, v) => b with { MiddleClickClose = v }),
        OfBar("keep_upper_gap", OptionValue.Bool, b => b.KeepUpperGap, (b, v) => b with { KeepUpperGap = v }),
        OfBar("font_family", OptionValue.Text, b => b.FontFamily, (b, v) => b with { FontFamily = v }),
        OfBar("font_size", OptionValue.Int(2, 64), b => b.FontSize, (b, v) => b with { FontSize = v }),
        OfBar("font_weight_active", Weight, b => b.FontWeightActive, (b, v) => b with { FontWeightActive = v }),
        OfBar("font_weight_inactive", Weight, b => b.FontWeightInactive, (b, v) => b with { FontWeightInactive = v }),
        OfBar("priority", OptionValue.Int(0, 6), b => b.Priority, (b, v) => b with { Priority = v }),
        OfBar("height", OptionValue.Int(1, 64), b => b.Height, (b, v) => b with { Height = v }),
        OfBar("indicator_height", OptionValue.Int(1, 64), b => b.IndicatorHeight, (b, v) => b with { IndicatorHeight = v }),
        OfBar("indicator_gap", OptionValue.Int(0, 64), b => b.IndicatorGap, (b, v) => b with { IndicatorGap = v }),
        OfBar("gaps_out", OptionValue.Int(0, 20), b => b.GapsOut, (b, v) => b with { GapsOut = v }),
        OfBar("gaps_in", OptionValue.Int(0, 20), b => b.GapsIn, (b, v) => b with { GapsIn = v }),
        OfBar("text_offset", OptionValue.Int(-20, 20), b => b.TextOffset, (b, v) => b with { TextOffset = v }),
        OfBar("text_padding", OptionValue.Int(0, 22), b => b.TextPadding, (b, v) => b with { TextPadding = v }),
        OfBar("text_color", OptionValue.Color, b => b.TextColor, (b, v) => b with { TextColor = v }),
        OfBar("text_color_inactive", OptionValue.Color, b => b.TitleColor(active: false, locked: false), (b, v) => b with { TextColorInactive = v }),
        OfBar("text_color_locked_active", OptionValue.Color, b => b.TitleColor(active: true, locked: true), (b, v) => b with { TextColorLockedActive = v }),
        OfBar("text_color_locked_inactive", OptionValue.Color, b => b.TitleColor(active: false, locked: true), (b, v) => b with { TextColorLockedInactive = v }),
        OfBar("col.active", OptionValue.Colors, b => [.. b.GradientColors(active: true, locked: false)], (b, v) => b with { Active = v[0], ActiveGradient = new BarGradient(v) }),
        OfBar("col.inactive", OptionValue.Colors, b => [.. b.GradientColors(active: false, locked: false)], (b, v) => b with { Inactive = v[0], InactiveGradient = new BarGradient(v) }),
        OfBar("col.locked_active", OptionValue.Colors, b => [.. b.GradientColors(active: true, locked: true)], (b, v) => b with { LockedActive = v[0], LockedActiveGradient = new BarGradient(v) }),
        OfBar("col.locked_inactive", OptionValue.Colors, b => [.. b.GradientColors(active: false, locked: true)], (b, v) => b with { LockedInactive = v[0], LockedInactiveGradient = new BarGradient(v) }),
        OfBar("stacked", OptionValue.Bool, b => b.Stacked, (b, v) => b with { Stacked = v }),
        OfBar("gradients", OptionValue.Bool, b => b.Gradients, (b, v) => b with { Gradients = v }),
        OfBar("rounding", OptionValue.Int(0, 40), b => b.Rounding, (b, v) => b with { Rounding = v }),
        OfBar("rounding_power", OptionValue.Number(1, 10), b => b.RoundingPower, (b, v) => b with { RoundingPower = v }),
        OfBar("round_only_edges", OptionValue.Bool, b => b.RoundOnlyEdges, (b, v) => b with { RoundOnlyEdges = v }),
        OfBar("gradient_rounding", OptionValue.Int(0, 40), b => b.GradientRounding, (b, v) => b with { GradientRounding = v }),
        OfBar("gradient_rounding_power", OptionValue.Number(1, 10), b => b.GradientRoundingPower, (b, v) => b with { GradientRoundingPower = v }),
        OfBar("gradient_round_only_edges", OptionValue.Bool, b => b.GradientRoundOnlyEdges, (b, v) => b with { GradientRoundOnlyEdges = v }),
    ]);

    public static GroupOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<GroupOptions> Of<T>(string name, OptionValue<T> value, Func<GroupOptions, T> get, Func<GroupOptions, T, GroupOptions> set) =>
        Option<GroupOptions>.Of(name, value, get, set);

    // An Option of the tab bar, under group:groupbar.
    private static Option<GroupOptions> OfBar<T>(string key, OptionValue<T> value, Func<GroupBarStyle, T> get, Func<GroupBarStyle, T, GroupBarStyle> set) =>
        Option<GroupOptions>.Of("group:groupbar:" + key, value, o => get(o.Bar), (o, v) => o with { Bar = set(o.Bar, v) });
}

/// <summary>How the tab bar over a Group's tile is drawn. Unset text colors fall back as the manual says.</summary>
public sealed record GroupBarStyle
{
    public static GroupBarStyle Documented { get; } = new();

    public bool Enabled { get; init; } = true;

    public bool DisableWhenOnly { get; init; }

    public bool RenderTitles { get; init; } = true;

    public bool Scrolling { get; init; } = true;

    public bool MiddleClickClose { get; init; } = true;

    public bool KeepUpperGap { get; init; } = true;

    public string FontFamily { get; init; } = "";

    public int FontSize { get; init; } = 8;

    public int FontWeightActive { get; init; } = 400;

    public int FontWeightInactive { get; init; } = 400;

    public int Height { get; init; } = 14;

    public int IndicatorHeight { get; init; } = 3;

    public int IndicatorGap { get; init; }

    public int GapsOut { get; init; } = 2;

    public int GapsIn { get; init; } = 2;

    public int TextOffset { get; init; }

    public int TextPadding { get; init; }

    public BorderColor TextColor { get; init; } = new(0xFF, 0xFF, 0xFF, 0xFF);

    public BorderColor? TextColorInactive { get; init; }

    public BorderColor? TextColorLockedActive { get; init; }

    public BorderColor? TextColorLockedInactive { get; init; }

    public BorderColor Active { get; init; } = new(0x66, 0xFF, 0xFF, 0x00);

    public BorderColor Inactive { get; init; } = new(0x66, 0x77, 0x77, 0x00);

    public BorderColor LockedActive { get; init; } = new(0x66, 0xFF, 0x55, 0x00);

    public BorderColor LockedInactive { get; init; } = new(0x66, 0x77, 0x55, 0x00);

    /// <summary>Every color <c>col.active</c> lists, which a gradient runs through. Null for the one default color.</summary>
    public BarGradient? ActiveGradient { get; init; }

    public BarGradient? InactiveGradient { get; init; }

    public BarGradient? LockedActiveGradient { get; init; }

    public BarGradient? LockedInactiveGradient { get; init; }

    /// <summary><c>stacked</c>: the tabs are rows, one above the other, instead of side by side.</summary>
    public bool Stacked { get; init; }

    /// <summary>
    /// <c>priority</c>: where the bar goes among the decorations on a window's top edge. The bar is the only one Winland
    /// draws there, so the value is kept and changes nothing.
    /// </summary>
    public int Priority { get; init; } = 3;

    /// <summary><c>gradients</c>: each tab's title row is filled with a gradient of its colors.</summary>
    public bool Gradients { get; init; }

    /// <summary><c>rounding</c>: the corner radius of the indicators, in pixels.</summary>
    public int Rounding { get; init; } = 1;

    public double RoundingPower { get; init; } = 2;

    /// <summary><c>round_only_edges</c>: with several tabs, only the outer corners of the first and last indicator are round.</summary>
    public bool RoundOnlyEdges { get; init; } = true;

    public int GradientRounding { get; init; } = 2;

    public double GradientRoundingPower { get; init; } = 2;

    public bool GradientRoundOnlyEdges { get; init; } = true;

    /// <summary>Multiplies the sizes, which the config gives in logical pixels, by a Monitor's scale.</summary>
    public GroupBarStyle ScaledBy(double scale)
    {
        if (scale is <= 0 or >= 1000 || !double.IsFinite(scale) || scale == 1)
            return this;

        int Scale(int value) => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        return this with
        {
            FontSize = Scale(FontSize),
            Height = Scale(Height),
            IndicatorHeight = Scale(IndicatorHeight),
            IndicatorGap = Scale(IndicatorGap),
            GapsOut = Scale(GapsOut),
            GapsIn = Scale(GapsIn),
            TextOffset = Scale(TextOffset),
            TextPadding = Scale(TextPadding),
        };
    }

    /// <summary>The strip the bar takes from the top of the tile: gaps, indicator and title row.</summary>
    public int Reserved =>
        (GapsOut * (KeepUpperGap ? 2 : 1)) + IndicatorHeight + IndicatorGap + (RenderTitles || Gradients ? Height : 0);

    /// <summary>One tab's row: gaps_out, the indicator, its gap and the title row.</summary>
    public int OneBarHeight => GapsOut + IndicatorHeight + IndicatorGap + (RenderTitles || Gradients ? Height : 0);

    /// <summary>The strip a bar of <paramref name="tabs"/> tabs takes: one row each when stacked, with the upper gap.</summary>
    public int ReservedFor(int tabs) =>
        Stacked ? (OneBarHeight * Math.Max(1, tabs)) + (KeepUpperGap ? GapsOut : 0) : Reserved;

    /// <summary>The title color of one tab.</summary>
    public BorderColor TitleColor(bool active, bool locked)
    {
        var inactive = TextColorInactive ?? TextColor;
        if (locked)
            return active ? TextColorLockedActive ?? TextColor : TextColorLockedInactive ?? inactive;

        return active ? TextColor : inactive;
    }

    /// <summary>The indicator color of one tab.</summary>
    public BorderColor IndicatorColor(bool active, bool locked) =>
        locked ? (active ? LockedActive : LockedInactive) : (active ? Active : Inactive);

    /// <summary>The colors the gradient of one tab runs through.</summary>
    public IReadOnlyList<BorderColor> GradientColors(bool active, bool locked)
    {
        var gradient = locked ? (active ? LockedActiveGradient : LockedInactiveGradient) : (active ? ActiveGradient : InactiveGradient);
        return gradient?.Colors ?? [IndicatorColor(active, locked)];
    }
}
