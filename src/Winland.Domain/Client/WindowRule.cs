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
