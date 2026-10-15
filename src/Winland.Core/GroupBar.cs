namespace Winland;

/// <summary>
/// The tab bar Winland draws over a Group's tile. <see cref="Bounds"/> is the strip in screen pixels.
/// Each tab names one member, in Group order. From the bottom of the strip up: the gap to the window,
/// the indicator, the indicator gap, the title row and, with <c>keep_upper_gap</c>, a top gap.
/// </summary>
public sealed record GroupBar(WindowId Shown, Rectangle Bounds, IReadOnlyList<GroupTab> Tabs, bool Locked, GroupBarStyle Style)
{
    /// <summary>True when both bars draw the same pixels.</summary>
    public bool SameAs(GroupBar? other) =>
        other is not null
        && other.Shown == Shown
        && other.Bounds == Bounds
        && other.Locked == Locked
        && other.Style == Style
        && other.Tabs.SequenceEqual(Tabs);

    /// <summary>The width of one tab. Tabs share the strip with <c>gaps_in</c> between them.</summary>
    public int TabWidth =>
        Tabs.Count == 0 ? 0 : Math.Max(0, (Bounds.Width - (Style.GapsIn * (Tabs.Count - 1))) / Tabs.Count);

    /// <summary>The indicator of tab <paramref name="index"/>, relative to the strip.</summary>
    public Rectangle IndicatorBounds(int index)
    {
        var y = Bounds.Height - Style.GapsOut - Style.IndicatorHeight;
        return new Rectangle(TabX(index), y, TabWidth, Style.IndicatorHeight);
    }

    /// <summary>The title row of tab <paramref name="index"/>, relative to the strip. Empty when titles are not drawn.</summary>
    public Rectangle TitleBounds(int index)
    {
        if (!Style.RenderTitles)
            return new Rectangle(TabX(index), 0, 0, 0);

        var y = IndicatorBounds(index).Y - Style.IndicatorGap - Style.Height;
        return new Rectangle(TabX(index), y, TabWidth, Style.Height);
    }

    /// <summary>The tab under a point relative to the strip. -1 on the gap between tabs or outside the strip.</summary>
    public int TabAt(int x, int y)
    {
        if (Tabs.Count == 0 || x < 0 || y < 0 || x >= Bounds.Width || y >= Bounds.Height)
            return -1;

        var step = TabWidth + Style.GapsIn;
        if (step <= 0)
            return -1;

        var index = x / step;
        if (index >= Tabs.Count || x - (index * step) >= TabWidth)
            return -1;

        return index;
    }

    private int TabX(int index) => index * (TabWidth + Style.GapsIn);
}

/// <summary>One member's tab. <see cref="Focused"/> marks the focused window, which the bar colors as active.</summary>
public readonly record struct GroupTab(WindowId Window, string Title, bool Focused);
