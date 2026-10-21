namespace Winland.Domain;

public sealed record WindowRule(
    WindowRuleEffect Effect,
    string? Workspace,
    string? ClassPattern,
    string? TitlePattern,
    string? ExePattern,
    string? AumidPattern = null,
    bool Enabled = true,
    Size? Size = null,
    double? ScrollingWidth = null,
    OpacityRule? Opacity = null,
    FullscreenRule? Fullscreen = null,
    RulePlacement? Placement = null,
    string? Group = null,
    IReadOnlyDictionary<string, object>? Props = null,
    IReadOnlyList<RuleMatch>? Dynamic = null,
    string? Text = null,
    string? InitialTitlePattern = null,
    string? InitialClassPattern = null);

/// <summary>One match on a window's state, such as <c>float:1</c> or <c>workspace:w[tv1]</c>.</summary>
public sealed record RuleMatch(RuleMatchKind Kind, string Value);

/// <summary>
/// The argument of a <c>move</c>, <c>size</c>, <c>monitor</c> or <c>no_close_for</c> rule.
/// </summary>
public sealed record RulePlacement(ExpressionPair? Expression = null, string? Monitor = null, long Milliseconds = 0);

/// <summary>
/// Where a new Client opens: <c>move</c> and <c>center</c> replace each other, so at most one is set.
/// </summary>
internal readonly record struct MapPlacement(
    ExpressionPair? Move = null,
    ExpressionPair? Size = null,
    bool Center = false,
    string? Monitor = null,
    long NoCloseFor = 0);

/// <summary>Internal and client modes from a <c>fullscreen_state</c> rule. The client mode is stored, not sent to the window.</summary>
public readonly record struct FullscreenRule(FullscreenMode Internal, FullscreenMode Client);
