using System.Globalization;

namespace Winland.Domain;

/// <summary>A min_size or max_size: a width and height in pixels, either of them infinite.</summary>
internal readonly record struct PropSize(double Width, double Height)
{
    public static PropSize Unbounded { get; } = new(double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>The size as a Layout limit. An infinite or out of range side is no limit.</summary>
    public Size? ToLimit() =>
        double.IsFinite(Width) && double.IsFinite(Height) && Width is >= 0 and <= int.MaxValue && Height is >= 0 and <= int.MaxValue
            ? new Size((int)Math.Round(Width), (int)Math.Round(Height))
            : null;
}
