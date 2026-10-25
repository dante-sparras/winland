namespace Winland.Domain;

/// <summary>The <c>group:*</c> and <c>group:groupbar:*</c> options, with the defaults of Winland.</summary>
public sealed record GroupOptions
{
    public static GroupOptions Documented { get; } = new();

    /// <summary><c>group:drag_into_group</c>: 0 never, 1 onto a member or its tab bar, 2 only onto the tab bar.</summary>
    public int DragIntoGroup { get; init; } = 1;

    public BorderColor ActiveBorder { get; init; } = new(0x66, 0xFF, 0xFF, 0x00);

    public BorderColor InactiveBorder { get; init; } = new(0x66, 0x77, 0x77, 0x00);

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
        Of("group:drag_into_group", OptionValue.Int(0, 2, DragIntoGroupNames), o => o.DragIntoGroup, (o, v) => o with { DragIntoGroup = v }),
        Of("group:col.border_active", OptionValue.Color, o => o.ActiveBorder, (o, v) => o with { ActiveBorder = v }),
        Of("group:col.border_inactive", OptionValue.Color, o => o.InactiveBorder, (o, v) => o with { InactiveBorder = v }),
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
        OfBar("text_color_inactive", OptionValue.Color, b => b.TitleColor(active: false), (b, v) => b with { TextColorInactive = v }),
        OfBar("col.active", OptionValue.Colors, b => [.. b.GradientColors(active: true)], (b, v) => b with { Active = v[0], ActiveGradient = new BarGradient(v) }),
        OfBar("col.inactive", OptionValue.Colors, b => [.. b.GradientColors(active: false)], (b, v) => b with { Inactive = v[0], InactiveGradient = new BarGradient(v) }),
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
