namespace Winland.Domain;

/// <summary>
/// One <c>workspace</c> rule, for the Workspace whose number it names. Unset fields stay null and do not override the
/// general config.
/// </summary>
public sealed record WorkspaceRule
{
    public static IReadOnlyList<WorkspaceRule> None { get; } = [];

    public string WorkspaceString { get; init; } = "";

    public string Monitor { get; init; } = "";

    public bool? Default { get; init; }

    public bool? Persistent { get; init; }

    public EdgeInsets? GapsIn { get; init; }

    public EdgeInsets? GapsOut { get; init; }

    /// <summary>The Layout this Workspace uses in place of <c>general:layout</c>.</summary>
    public string? Layout { get; init; }

    internal WorkspaceRule Merge(WorkspaceRule other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return this with
        {
            Monitor = other.Monitor.Length > 0 ? other.Monitor : Monitor,
            Default = other.Default ?? Default,
            Persistent = other.Persistent ?? Persistent,
            GapsIn = other.GapsIn ?? GapsIn,
            GapsOut = other.GapsOut ?? GapsOut,
            Layout = other.Layout ?? Layout,
        };
    }
}
