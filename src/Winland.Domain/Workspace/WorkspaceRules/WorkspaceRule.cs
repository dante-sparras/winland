namespace Winland.Domain;

/// <summary>
/// One <c>workspace</c> rule. Unset fields stay null and do not override the general config.
/// </summary>
public sealed record WorkspaceRule
{
    public static IReadOnlyList<WorkspaceRule> None { get; } = [];

    public string WorkspaceString { get; init; } = "";

    public string WorkspaceName { get; init; } = "";

    public string Monitor { get; init; } = "";

    public bool? Default { get; init; }

    public bool? Persistent { get; init; }

    public EdgeInsets? GapsIn { get; init; }

    public EdgeInsets? GapsOut { get; init; }

    /// <summary>The gaps between a floating Client and the Monitor edges, in place of <c>general:float_gaps</c>.</summary>
    public EdgeInsets? FloatGaps { get; init; }

    public int? BorderSize { get; init; }

    public bool? Border { get; init; }

    public bool? Rounding { get; init; }

    public bool? Decorate { get; init; }

    /// <summary>The style this Workspace is switched to and from with, in place of the workspaces node's.</summary>
    public string? Animation { get; init; }

    public string? DefaultName { get; init; }

    public string? OnCreatedEmpty { get; init; }

    /// <summary>The Layout this Workspace uses in place of <c>general:layout</c>.</summary>
    public string? Layout { get; init; }

    public IReadOnlyDictionary<string, string> LayoutOpts { get; init; } = EmptyOpts;

    private static readonly IReadOnlyDictionary<string, string> EmptyOpts =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal WorkspaceRule Merge(WorkspaceRule other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var opts = new Dictionary<string, string>(LayoutOpts, StringComparer.Ordinal);
        foreach (var pair in other.LayoutOpts)
            opts[pair.Key] = pair.Value;

        return this with
        {
            Monitor = other.Monitor.Length > 0 ? other.Monitor : Monitor,
            Default = other.Default ?? Default,
            Persistent = other.Persistent ?? Persistent,
            GapsIn = other.GapsIn ?? GapsIn,
            GapsOut = other.GapsOut ?? GapsOut,
            FloatGaps = other.FloatGaps ?? FloatGaps,
            BorderSize = other.BorderSize ?? BorderSize,
            Border = other.Border ?? Border,
            Rounding = other.Rounding ?? Rounding,
            Decorate = other.Decorate ?? Decorate,
            Animation = other.Animation ?? Animation,
            DefaultName = other.DefaultName ?? DefaultName,
            OnCreatedEmpty = other.OnCreatedEmpty ?? OnCreatedEmpty,
            Layout = other.Layout ?? Layout,
            LayoutOpts = opts.Count == 0 ? EmptyOpts : opts,
        };
    }
}
