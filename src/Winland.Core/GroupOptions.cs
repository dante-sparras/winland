using System.Globalization;
using static Winland.ConfigValueSyntax;

namespace Winland;

/// <summary>The <c>group:*</c> and <c>group:groupbar:*</c> options, with the defaults of the pinned release.</summary>
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

    // Adding an entry here applies that keyword and stops the config warning.
    private static readonly Dictionary<string, Func<GroupOptions, string, GroupOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["group:auto_group"] = (o, v) => TryParseBool(v, out var b) ? o with { AutoGroup = b } : o,
        ["group:insert_after_current"] = (o, v) => TryParseBool(v, out var b) ? o with { InsertAfterCurrent = b } : o,
        ["group:focus_removed_window"] = (o, v) => TryParseBool(v, out var b) ? o with { FocusRemovedWindow = b } : o,
        ["group:group_on_movetoworkspace"] = (o, v) => TryParseBool(v, out var b) ? o with { GroupOnMoveToWorkspace = b } : o,
        ["binds:ignore_group_lock"] = (o, v) => TryParseBool(v, out var b) ? o with { IgnoreGroupLock = b } : o,
        ["binds:movefocus_cycles_groupfirst"] = (o, v) => TryParseBool(v, out var b) ? o with { MoveFocusCyclesGroupFirst = b } : o,
        ["group:drag_into_group"] = (o, v) => TryInt(v, 0, 2, out var i) ? o with { DragIntoGroup = i } : o,
        ["group:merge_groups_on_drag"] = (o, v) => TryParseBool(v, out var b) ? o with { MergeGroupsOnDrag = b } : o,
        ["group:merge_groups_on_groupbar"] = (o, v) => TryParseBool(v, out var b) ? o with { MergeGroupsOnGroupbar = b } : o,
        ["group:merge_floated_into_tiled_on_groupbar"] = (o, v) => TryParseBool(v, out var b) ? o with { MergeFloatedIntoTiledOnGroupbar = b } : o,
        ["group:col.border_active"] = (o, v) => TryColor(v, out var c) ? o with { ActiveBorder = c } : o,
        ["group:col.border_inactive"] = (o, v) => TryColor(v, out var c) ? o with { InactiveBorder = c } : o,
        ["group:col.border_locked_active"] = (o, v) => TryColor(v, out var c) ? o with { LockedActiveBorder = c } : o,
        ["group:col.border_locked_inactive"] = (o, v) => TryColor(v, out var c) ? o with { LockedInactiveBorder = c } : o,
        ["general:col.nogroup_border"] = (o, v) => TryColor(v, out var c) ? o with { NoGroupBorder = c } : o,
        ["general:col.nogroup_border_active"] = (o, v) => TryColor(v, out var c) ? o with { NoGroupActiveBorder = c } : o,
        ["group:groupbar:enabled"] = (o, v) => TryParseBool(v, out var b) ? o.WithBar(o.Bar with { Enabled = b }) : o,
        ["group:groupbar:disable_when_only"] = (o, v) => TryParseBool(v, out var b) ? o.WithBar(o.Bar with { DisableWhenOnly = b }) : o,
        ["group:groupbar:render_titles"] = (o, v) => TryParseBool(v, out var b) ? o.WithBar(o.Bar with { RenderTitles = b }) : o,
        ["group:groupbar:scrolling"] = (o, v) => TryParseBool(v, out var b) ? o.WithBar(o.Bar with { Scrolling = b }) : o,
        ["group:groupbar:middle_click_close"] = (o, v) => TryParseBool(v, out var b) ? o.WithBar(o.Bar with { MiddleClickClose = b }) : o,
        ["group:groupbar:keep_upper_gap"] = (o, v) => TryParseBool(v, out var b) ? o.WithBar(o.Bar with { KeepUpperGap = b }) : o,
        ["group:groupbar:font_family"] = (o, v) => o.WithBar(o.Bar with { FontFamily = v.Trim() == "[[EMPTY]]" ? "" : v.Trim() }),
        ["group:groupbar:font_size"] = (o, v) => TryInt(v, 2, 64, out var i) ? o.WithBar(o.Bar with { FontSize = i }) : o,
        ["group:groupbar:font_weight_active"] = (o, v) => TryWeight(v, out var w) ? o.WithBar(o.Bar with { FontWeightActive = w }) : o,
        ["group:groupbar:font_weight_inactive"] = (o, v) => TryWeight(v, out var w) ? o.WithBar(o.Bar with { FontWeightInactive = w }) : o,
        ["group:groupbar:height"] = (o, v) => TryInt(v, 1, 64, out var i) ? o.WithBar(o.Bar with { Height = i }) : o,
        ["group:groupbar:indicator_height"] = (o, v) => TryInt(v, 1, 64, out var i) ? o.WithBar(o.Bar with { IndicatorHeight = i }) : o,
        ["group:groupbar:indicator_gap"] = (o, v) => TryInt(v, 0, 64, out var i) ? o.WithBar(o.Bar with { IndicatorGap = i }) : o,
        ["group:groupbar:gaps_out"] = (o, v) => TryInt(v, 0, 20, out var i) ? o.WithBar(o.Bar with { GapsOut = i }) : o,
        ["group:groupbar:gaps_in"] = (o, v) => TryInt(v, 0, 20, out var i) ? o.WithBar(o.Bar with { GapsIn = i }) : o,
        ["group:groupbar:text_offset"] = (o, v) => TryInt(v, -20, 20, out var i) ? o.WithBar(o.Bar with { TextOffset = i }) : o,
        ["group:groupbar:text_padding"] = (o, v) => TryInt(v, 0, 22, out var i) ? o.WithBar(o.Bar with { TextPadding = i }) : o,
        ["group:groupbar:text_color"] = (o, v) => TryColor(v, out var c) ? o.WithBar(o.Bar with { TextColor = c }) : o,
        ["group:groupbar:text_color_inactive"] = (o, v) => TryColor(v, out var c) ? o.WithBar(o.Bar with { TextColorInactive = c }) : o,
        ["group:groupbar:text_color_locked_active"] = (o, v) => TryColor(v, out var c) ? o.WithBar(o.Bar with { TextColorLockedActive = c }) : o,
        ["group:groupbar:text_color_locked_inactive"] = (o, v) => TryColor(v, out var c) ? o.WithBar(o.Bar with { TextColorLockedInactive = c }) : o,
        ["group:groupbar:col.active"] = (o, v) => TryColors(v, out var c) ? o.WithBar(o.Bar with { Active = c[0], ActiveGradient = new BarGradient(c) }) : o,
        ["group:groupbar:col.inactive"] = (o, v) => TryColors(v, out var c) ? o.WithBar(o.Bar with { Inactive = c[0], InactiveGradient = new BarGradient(c) }) : o,
        ["group:groupbar:col.locked_active"] = (o, v) => TryColors(v, out var c) ? o.WithBar(o.Bar with { LockedActive = c[0], LockedActiveGradient = new BarGradient(c) }) : o,
        ["group:groupbar:col.locked_inactive"] = (o, v) => TryColors(v, out var c) ? o.WithBar(o.Bar with { LockedInactive = c[0], LockedInactiveGradient = new BarGradient(c) }) : o,
        ["group:groupbar:gradients"] = (o, v) => TryParseBool(v, out var b) ? o.WithBar(o.Bar with { Gradients = b }) : o,
        ["group:groupbar:rounding"] = (o, v) => TryInt(v, 0, 40, out var i) ? o.WithBar(o.Bar with { Rounding = i }) : o,
        ["group:groupbar:rounding_power"] = (o, v) => TryPower(v, out var p) ? o.WithBar(o.Bar with { RoundingPower = p }) : o,
        ["group:groupbar:round_only_edges"] = (o, v) => TryParseBool(v, out var b) ? o.WithBar(o.Bar with { RoundOnlyEdges = b }) : o,
        ["group:groupbar:gradient_rounding"] = (o, v) => TryInt(v, 0, 40, out var i) ? o.WithBar(o.Bar with { GradientRounding = i }) : o,
        ["group:groupbar:gradient_rounding_power"] = (o, v) => TryPower(v, out var p) ? o.WithBar(o.Bar with { GradientRoundingPower = p }) : o,
        ["group:groupbar:gradient_round_only_edges"] = (o, v) => TryParseBool(v, out var b) ? o.WithBar(o.Bar with { GradientRoundOnlyEdges = b }) : o,
    };

    internal static IReadOnlySet<string> Keywords { get; } = Appliers.Keys.ToHashSet(StringComparer.Ordinal);

    public static GroupOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented;
        foreach (var setting in document.Settings)
        {
            if (Appliers.TryGetValue(setting.Keyword, out var apply))
                options = apply(options, setting.Value);
        }

        return options;
    }

    private GroupOptions WithBar(GroupBarStyle bar) => this with { Bar = bar };

    private static bool TryColor(string text, out BorderColor color) => BorderColor.TryParse(text, out color, out _);

    private static bool TryColors(string text, out BorderColor[] colors) =>
        ConfigValueSyntax.TryParseColors(text, out colors) && colors.Length > 0;

    // A corner's rounding power, from 1 to 10, where 2 is a circle.
    private static bool TryPower(string text, out double power)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out power) || !double.IsFinite(power))
            return false;

        power = Math.Clamp(power, 1, 10);
        return true;
    }

    private static bool TryInt(string text, int min, int max, out int value)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return false;

        value = Math.Clamp(value, min, max);
        return true;
    }

    // A font weight is a name or a number from 100 to 1000.
    private static bool TryWeight(string text, out int weight)
    {
        weight = text.Trim().ToLowerInvariant() switch
        {
            "thin" => 100,
            "ultralight" => 200,
            "light" => 300,
            "book" => 380,
            "normal" => 400,
            "medium" => 500,
            "semibold" => 600,
            "bold" => 700,
            "ultrabold" => 800,
            "heavy" => 900,
            "ultraheavy" => 1000,
            _ => 0,
        };
        if (weight > 0)
            return true;

        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out weight))
            return false;

        weight = Math.Clamp(weight, 100, 1000);
        return true;
    }
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
