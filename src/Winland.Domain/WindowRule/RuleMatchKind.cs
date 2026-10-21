namespace Winland.Domain;

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
