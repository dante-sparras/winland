namespace Winland.Domain;

/// <summary>
/// What the min_size and max_size window rules ask of a Client. A floating Client keeps the rule's sizes in
/// place of the window's own. A tiled Client keeps only the window's own.
/// </summary>
internal readonly record struct SizeRule(Size? Min, Size? Max)
{
    /// <summary>The limits a floating Client keeps, given the window's own.</summary>
    public SizeLimits Floating(Size? ownMin, Size? ownMax) =>
        new(Min ?? ownMin, Max ?? ownMax);
}
