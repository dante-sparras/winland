using System.Globalization;

namespace Winland.Domain;

public sealed record Dispatch(string Name, string Arguments = "") : Command
{
    /// <summary>The key edge that ran this dispatcher from a bind. Null for a pipe command.</summary>
    public BindTrigger? Trigger { get; init; }
}
