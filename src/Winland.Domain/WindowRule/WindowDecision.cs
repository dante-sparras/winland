namespace Winland.Domain;

internal readonly record struct WindowDecision(
    bool Adopt,
    bool Floating,
    string? Workspace,
    bool NoInitialFocus = false,
    bool NoFocus = false,
    Size? MinSize = null,
    Size? MaxSize = null,
    double? ScrollingWidth = null,
    OpacityRule? Opacity = null,
    bool Opaque = false,
    FullscreenRule? FullscreenRequest = null,
    bool Pin = false,
    MapPlacement Placement = default,
    SizeRule Sizes = default,
    IReadOnlyDictionary<string, object>? Props = null,
    ExpressionPair? MinSizeExpression = null,
    ExpressionPair? MaxSizeExpression = null);
