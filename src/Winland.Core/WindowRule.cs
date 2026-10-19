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
    Prop,
    Tag,
    Content,
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
    string? Group = null,
    IReadOnlyDictionary<string, object>? Props = null,
    IReadOnlyList<RuleMatch>? Dynamic = null,
    string? Text = null);

/// <summary>What a window rule matches on that can change while the window lives.</summary>
public enum RuleMatchKind
{
    Float,
    Fullscreen,
    Pin,
    Focus,
    Group,
    Modal,
    FullscreenStateInternal,
    FullscreenStateClient,
    Workspace,
    Tag,
    Content,
}

/// <summary>One match on a window's state, such as <c>float:1</c> or <c>workspace:w[tv1]</c>.</summary>
public sealed record RuleMatch(RuleMatchKind Kind, string Value);

/// <summary>
/// The state of a window a rule's dynamic matches read. <paramref name="OnWorkspace"/> answers whether the window's
/// Workspace matches a workspace selector.
/// </summary>
internal readonly record struct RuleSubject(
    bool Floating,
    int FullscreenInternal,
    int FullscreenClient,
    bool Pinned,
    bool Focused,
    bool Grouped,
    Func<string, bool> OnWorkspace,
    Func<string, bool> Tagged,
    int Content = 0,
    bool Modal = false);

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
    GroupRuleFlags Group = GroupRuleFlags.None,
    SizeRule Sizes = default,
    IReadOnlyDictionary<string, object>? Props = null,
    int Content = 0,
    ExpressionPair? MinSizeExpression = null,
    ExpressionPair? MaxSizeExpression = null);

/// <summary>
/// What the min_size, max_size and no_max_size window rules ask of a Client. A floating Client keeps the rule's sizes in
/// place of the window's own. A tiled Client keeps them only with <c>misc:size_limits_tiled</c>, as in the pinned release.
/// </summary>
internal readonly record struct SizeRule(Size? Min, Size? Max, bool NoMax)
{
    /// <summary>The limits a floating Client keeps, given the window's own.</summary>
    public SizeLimits Floating(Size? ownMin, Size? ownMax) =>
        new(Min ?? ownMin, Max ?? (NoMax ? null : ownMax));

    /// <summary>The limits a tiled Client keeps, given the window's own.</summary>
    public TiledLimits Tiled(Size? ownMin, Size? ownMax, bool sizeLimitsTiled)
    {
        var limits = sizeLimitsTiled ? Floating(ownMin, ownMax) : new SizeLimits(ownMin, NoMax ? null : ownMax);
        return new TiledLimits(limits.Min, limits.Max);
    }
}
