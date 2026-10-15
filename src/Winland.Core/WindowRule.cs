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
    IdleInhibitMode IdleInhibit = IdleInhibitMode.None);

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
    IdleInhibitMode IdleInhibit = IdleInhibitMode.None);
