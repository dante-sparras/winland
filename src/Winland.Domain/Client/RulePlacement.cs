namespace Winland.Domain;

/// <summary>
/// The argument of a <c>move</c>, <c>size</c>, <c>monitor</c> or <c>no_close_for</c> rule.
/// </summary>
public sealed record RulePlacement(ExpressionPair? Expression = null, string? Monitor = null, long Milliseconds = 0);
