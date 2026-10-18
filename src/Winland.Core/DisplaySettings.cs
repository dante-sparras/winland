using System.Globalization;

namespace Winland;

/// <summary>
/// A monitor rule's mode for one display: <c>highres</c>, <c>highrr</c>, <c>maxwidth</c>, or <c>WIDTHxHEIGHT</c>
/// with an optional <c>@RATE</c>. <c>preferred</c> asks for nothing.
/// </summary>
public readonly record struct DisplayModeRequest(string Device, string Mode);

/// <summary>A monitor rule's scale for one display.</summary>
public readonly record struct DisplayScaleRequest(string Device, double Scale);

/// <summary>One mode a display offers: its size in pixels and its refresh rate.</summary>
public readonly record struct DisplayModeChoice(int Width, int Height, int Rate);

/// <summary>Picks a display mode and a scale the way a monitor rule asks for them.</summary>
public static class DisplaySettings
{
    /// <summary>The scales Windows offers, in percent.</summary>
    public static IReadOnlyList<int> ScalePercents { get; } = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    /// <summary>Whether <paramref name="text"/> is a mode a monitor rule can ask for.</summary>
    public static bool IsMode(string text) =>
        text.Equals("highres", StringComparison.OrdinalIgnoreCase)
        || text.Equals("highrr", StringComparison.OrdinalIgnoreCase)
        || text.Equals("maxwidth", StringComparison.OrdinalIgnoreCase)
        || TrySize(text, out _, out _, out _);

    /// <summary>
    /// The mode <paramref name="mode"/> picks from <paramref name="offered"/>, or null when none fits. <c>highres</c> takes
    /// the most pixels, then the fastest rate; <c>highrr</c> the fastest rate, then the most pixels; <c>maxwidth</c> the
    /// widest, then the fastest; a size takes that size at the rate nearest the one asked for, or the fastest without one.
    /// </summary>
    public static DisplayModeChoice? Choose(IReadOnlyList<DisplayModeChoice> offered, string mode)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(mode);
        if (offered.Count == 0)
            return null;

        if (mode.Equals("highres", StringComparison.OrdinalIgnoreCase))
            return offered.OrderByDescending(choice => (long)choice.Width * choice.Height).ThenByDescending(choice => choice.Rate).First();

        if (mode.Equals("highrr", StringComparison.OrdinalIgnoreCase))
            return offered.OrderByDescending(choice => choice.Rate).ThenByDescending(choice => (long)choice.Width * choice.Height).First();

        if (mode.Equals("maxwidth", StringComparison.OrdinalIgnoreCase))
            return offered.OrderByDescending(choice => choice.Width).ThenByDescending(choice => choice.Rate).First();

        if (!TrySize(mode, out var width, out var height, out var rate))
            return null;

        var sized = offered.Where(choice => choice.Width == width && choice.Height == height).ToList();
        if (sized.Count == 0)
            return null;

        return rate is double wanted
            ? sized.OrderBy(choice => Math.Abs(choice.Rate - wanted)).ThenByDescending(choice => choice.Rate).First()
            : sized.OrderByDescending(choice => choice.Rate).First();
    }

    /// <summary>The Windows scale, in percent, nearest <paramref name="scale"/>.</summary>
    public static int NearestPercent(double scale) =>
        ScalePercents.OrderBy(percent => Math.Abs(percent - (scale * 100))).ThenBy(percent => percent).First();

    private static bool TrySize(string text, out int width, out int height, out double? rate)
    {
        width = 0;
        height = 0;
        rate = null;
        var at = text.IndexOf('@', StringComparison.Ordinal);
        var size = at < 0 ? text : text[..at];
        var times = size.IndexOf('x', StringComparison.OrdinalIgnoreCase);
        if (times <= 0
            || !int.TryParse(size[..times], NumberStyles.None, CultureInfo.InvariantCulture, out width)
            || !int.TryParse(size[(times + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out height)
            || width <= 0
            || height <= 0)
            return false;

        if (at < 0)
            return true;

        var hz = text[(at + 1)..];
        if (hz.EndsWith("hz", StringComparison.OrdinalIgnoreCase))
            hz = hz[..^2];

        if (!double.TryParse(hz, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0 || !double.IsFinite(parsed))
            return false;

        rate = parsed;
        return true;
    }
}
