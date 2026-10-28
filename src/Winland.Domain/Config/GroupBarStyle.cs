namespace Winland.Domain;

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

    public BorderColor Active { get; init; } = new(0x66, 0xFF, 0xFF, 0x00);

    public BorderColor Inactive { get; init; } = new(0x66, 0x77, 0x77, 0x00);

    /// <summary>Every color <c>col.active</c> lists, which a gradient runs through. Null for the one default color.</summary>
    public BarGradient? ActiveGradient { get; init; }

    public BarGradient? InactiveGradient { get; init; }

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

    /// <summary>
    /// Multiplies the sizes, which the config gives in logical pixels, by a Monitor's scale. The font and the title row
    /// also grow by <paramref name="text"/>, the text size Windows' accessibility settings ask for.
    /// </summary>
    public GroupBarStyle ScaledBy(double scale, double text = 1)
    {
        if (scale is <= 0 or >= 1000 || !double.IsFinite(scale))
            scale = 1;
        if (text is < 1 or > 10 || !double.IsFinite(text))
            text = 1;
        if (scale == 1 && text == 1)
            return this;

        int Scale(int value) => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        int ScaleText(int value) => (int)Math.Round(value * scale * text, MidpointRounding.AwayFromZero);
        return this with
        {
            FontSize = ScaleText(FontSize),
            Height = ScaleText(Height),
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
    public BorderColor TitleColor(bool active) => active ? TextColor : TextColorInactive ?? TextColor;

    /// <summary>The indicator color of one tab.</summary>
    public BorderColor IndicatorColor(bool active) => active ? Active : Inactive;

    /// <summary>The colors the gradient of one tab runs through.</summary>
    public IReadOnlyList<BorderColor> GradientColors(bool active) =>
        (active ? ActiveGradient : InactiveGradient)?.Colors ?? [IndicatorColor(active)];
}

/// <summary>The colors of a tab bar gradient, as the config lists them. Two are equal when their colors are.</summary>
public sealed record BarGradient(IReadOnlyList<BorderColor> Colors)
{
    public bool Equals(BarGradient? other) => other is not null && Colors.SequenceEqual(other.Colors);

    public override int GetHashCode() => Colors.Count == 0 ? 0 : HashCode.Combine(Colors.Count, Colors[0]);
}
