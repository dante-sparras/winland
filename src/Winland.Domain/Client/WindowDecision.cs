namespace Winland.Domain;

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
    MapPlacement Placement = default,
    GroupRuleFlags Group = GroupRuleFlags.None,
    SizeRule Sizes = default,
    IReadOnlyDictionary<string, object>? Props = null,
    int Content = 0,
    ExpressionPair? MinSizeExpression = null,
    ExpressionPair? MaxSizeExpression = null,
    // A fullscreen_state rule matched. While it matches, the window-rule layer of sync_fullscreen is false.
    bool StatedFullscreen = false);
