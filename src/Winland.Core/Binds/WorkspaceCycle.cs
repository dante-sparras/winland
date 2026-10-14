namespace Winland.Core.Binds;

/// <summary>Which way a workspace cycle goes.</summary>
public enum WorkspaceCycle
{
    /// <summary>The next workspace that exists, and the first after the last.</summary>
    Next,

    /// <summary>The previous workspace that exists, and the last before the first.</summary>
    Previous,

    /// <summary>The workspace that had the keyboard before the last change.</summary>
    Former,
}
