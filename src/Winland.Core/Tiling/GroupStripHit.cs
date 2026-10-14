namespace Winland.Core.Tiling;

/// <summary>The size of a group's strip. Winland insets the tile by this and draws the strip that tall.</summary>
public static class GroupStripMetrics
{
    /// <summary>Strip height, in device-independent pixels.</summary>
    public const int HeightDips = 28;

    /// <summary>Physical pixels for <paramref name="dpi"/>. A DPI of 0 is treated as 96.</summary>
    /// <param name="dpi">The system DPI.</param>
    /// <returns>At least one pixel.</returns>
    public static int Pixels(uint dpi)
    {
        var scale = dpi == 0 ? 96u : dpi;
        return Math.Max(1, (int)Math.Ceiling(HeightDips * scale / 96.0));
    }
}

/// <summary>Which tab of a group's strip a click landed on. Tabs share the width, left to right.</summary>
public static class GroupStripHit
{
    /// <summary>The tab under <paramref name="x"/>.</summary>
    /// <param name="x">The click, in pixels from the left of the strip.</param>
    /// <param name="width">The strip's width, in pixels.</param>
    /// <param name="tabs">How many tabs the strip has.</param>
    /// <returns>The tab index, or -1 when the strip has no tabs or the click is left of it.</returns>
    public static int Tab(int x, int width, int tabs)
    {
        if (tabs <= 0 || width <= 0 || x < 0)
        {
            return -1;
        }

        if (x >= width)
        {
            return tabs - 1;
        }

        var index = (int)((long)x * tabs / width);
        return index >= tabs ? tabs - 1 : index;
    }
}
