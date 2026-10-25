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
    IReadOnlyDictionary<string, object>? Props = null,
    string? InitialTitlePattern = null,
    string? InitialClassPattern = null);

/// <summary>
/// The argument of a <c>move</c>, <c>size</c> or <c>monitor</c> rule.
/// </summary>
public sealed record RulePlacement(ExpressionPair? Expression = null, string? Monitor = null);

/// <summary>
/// Where a new Client opens: <c>move</c> and <c>center</c> replace each other, so at most one is set.
/// </summary>
internal readonly record struct MapPlacement(
    ExpressionPair? Move = null,
    ExpressionPair? Size = null,
    bool Center = false,
    string? Monitor = null);

/// <summary>Internal and client modes from a <c>fullscreen</c> or <c>maximize</c> rule. The client mode is stored, not sent to the window.</summary>
public readonly record struct FullscreenRule(FullscreenMode Internal, FullscreenMode Client);
