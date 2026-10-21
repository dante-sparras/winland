namespace Winland.Domain;

/// <summary>
/// The tab bar Winland draws over a Group's tile. <see cref="Bounds"/> is the strip in screen pixels.
/// Each tab names one member, in Group order. From the bottom of the strip up: the gap to the window,
/// the indicator, the indicator gap, the title row and, with <c>keep_upper_gap</c>, a top gap. A stacked bar
/// (<c>stacked</c>) gives each member such a row across the whole width, the first member at the top.
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

    /// <summary>The width of one tab. Tabs share the strip with <c>gaps_in</c> between them, or each takes it all when stacked.</summary>
    public int TabWidth =>
        Tabs.Count == 0 ? 0
        : Style.Stacked ? Bounds.Width
        : Math.Max(0, (Bounds.Width - (Style.GapsIn * (Tabs.Count - 1))) / Tabs.Count);

    /// <summary>
    /// Where tab <paramref name="index"/> comes in the order Winland draws the tabs: left to right, or from
    /// the bottom up when stacked. Its rounding follows that order.
    /// </summary>
    public int DrawOrder(int index) => Style.Stacked ? Tabs.Count - 1 - index : index;

    /// <summary>The indicator of tab <paramref name="index"/>, relative to the strip.</summary>
    public Rectangle IndicatorBounds(int index)
    {
        var y = Bounds.Height - Lift(index) - Style.GapsOut - Style.IndicatorHeight;
        return new Rectangle(TabX(index), y, TabWidth, Style.IndicatorHeight);
    }

    /// <summary>The title row of tab <paramref name="index"/>, relative to the strip. Empty when neither titles nor gradients are drawn.</summary>
    public Rectangle TitleBounds(int index)
    {
        if (!Style.RenderTitles && !Style.Gradients)
            return new Rectangle(TabX(index), 0, 0, 0);

        var y = Bounds.Height - Lift(index) - Style.OneBarHeight;
        return new Rectangle(TabX(index), y, TabWidth, Style.Height);
    }

    /// <summary>The tab under a point relative to the strip. -1 on the gap between tabs or outside the strip.</summary>
    public int TabAt(int x, int y)
    {
        if (Tabs.Count == 0 || x < 0 || y < 0 || x >= Bounds.Width || y >= Bounds.Height)
            return -1;

        // A stacked bar is cut into rows of Winland's bar height plus gaps_out, with that gap at each row's top.
        if (Style.Stacked)
        {
            var row = RowStep;
            if (row <= 0)
                return -1;

            var at = (int)(y / row);
            return at >= Tabs.Count || y - (row * at) < Style.GapsOut ? -1 : at;
        }

        var step = TabWidth + Style.GapsIn;
        if (step <= 0)
            return -1;

        var index = x / step;
        if (index >= Tabs.Count || x - (index * step) >= TabWidth)
            return -1;

        return index;
    }

    /// <summary>
    /// The place a window dropped at <paramref name="pointer"/>, in screen pixels, joins: the tab after the one whose
    /// middle it passed, as Winland counts it.
    /// </summary>
    public int DropIndex(Point pointer)
    {
        double relative;
        double step;
        if (Style.Stacked)
        {
            step = RowStep;
            relative = pointer.Y - Bounds.Y - (step / 2);
        }
        else
        {
            step = TabWidth + Style.GapsIn;
            relative = pointer.X - Bounds.X - (TabWidth / 2);
        }

        return (relative < 0 || step <= 0 ? -1 : (int)(relative / step)) + 1;
    }

    // Winland's stacked row: the bar height it divides the strip into, plus gaps_out.
    private double RowStep =>
        Tabs.Count == 0 ? 0
        : (((Bounds.Height - (Style.GapsOut * (Style.KeepUpperGap ? 1 : 0))) - (Style.GapsOut * Tabs.Count)) / (double)Tabs.Count) + Style.GapsOut;

    // How far up from the bottom of the strip tab index's row sits.
    private int Lift(int index) => Style.Stacked ? (Tabs.Count - 1 - index) * Style.OneBarHeight : 0;

    private int TabX(int index) => Style.Stacked ? 0 : index * (TabWidth + Style.GapsIn);
}
