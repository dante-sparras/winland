namespace Winland.Domain;

/// <summary>
/// Where a new Client opens: <c>move</c> and <c>center</c> replace each other, so at most one is set.
/// </summary>
internal readonly record struct MapPlacement(
    ExpressionPair? Move = null,
    ExpressionPair? Size = null,
    bool Center = false,
    string? Monitor = null,
    long NoCloseFor = 0);
