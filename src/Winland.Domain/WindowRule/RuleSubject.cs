namespace Winland.Domain;

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
