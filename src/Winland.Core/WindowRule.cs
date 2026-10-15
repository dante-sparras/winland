namespace Winland;

public enum WindowRuleEffect
{
    Float,
    Tile,
    Workspace,
    Ignore,
    NoInitialFocus,
    NoFocus,
    StayFocused,
    MinSize,
    MaxSize,
    NoMaxSize,
    ScrollingWidth,
    Opacity,
    Opaque,
    Fullscreen,
    Maximize,
    FullscreenState,
    Pin,
    IdleInhibit,
    Move,
    Size,
    Center,
    Monitor,
    NoCloseFor,
    Group,
}

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
    IdleInhibitMode IdleInhibit = IdleInhibitMode.None,
    RulePlacement? Placement = null,
    string? Group = null);

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

internal readonly record struct WindowDecision(
    bool Adopt,
    bool Floating,
    string? Workspace,
    bool NoInitialFocus = false,
    bool NoFocus = false,
    bool StayFocused = false,
    Size? MinSize = null,
    Size? MaxSize = null,
    double? ScrollingWidth = null,
    OpacityRule? Opacity = null,
    bool Opaque = false,
    FullscreenRule? FullscreenRequest = null,
    bool Pin = false,
    IdleInhibitMode IdleInhibit = IdleInhibitMode.None,
    MapPlacement Placement = default,
    GroupRuleFlags Group = GroupRuleFlags.None);
