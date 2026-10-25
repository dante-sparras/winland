namespace Winland.Domain;

/// <summary>
/// What the min_size and max_size window rules ask of a Client. A floating Client keeps the rule's sizes in
/// place of the window's own. A tiled Client keeps them only with <c>misc:size_limits_tiled</c>.
/// </summary>
internal readonly record struct SizeRule(Size? Min, Size? Max)
{
    /// <summary>The limits a floating Client keeps, given the window's own.</summary>
    public SizeLimits Floating(Size? ownMin, Size? ownMax) =>
        new(Min ?? ownMin, Max ?? ownMax);

    /// <summary>The limits a tiled Client keeps, given the window's own.</summary>
    public TiledLimits Tiled(Size? ownMin, Size? ownMax, bool sizeLimitsTiled)
    {
        var limits = sizeLimitsTiled ? Floating(ownMin, ownMax) : new SizeLimits(ownMin, ownMax);
        return new TiledLimits(limits.Min, limits.Max);
    }
}
