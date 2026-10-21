namespace Winland.Domain;

/// <summary>One match on a window's state, such as <c>float:1</c> or <c>workspace:w[tv1]</c>.</summary>
public sealed record RuleMatch(RuleMatchKind Kind, string Value);
