namespace Winland.Core.Binds;

/// <summary>One chord mapped to a dispatcher.</summary>
/// <param name="Chord">The chord that fires it.</param>
/// <param name="To">Where the chord goes.</param>
/// <param name="Hold">True when both key edges are delivered. A tap delivers the down edge only.</param>
/// <param name="While">The condition that must be asked before this bind is visible, or null when it is always on.</param>
public sealed record Bind(Chord Chord, Dispatch To, bool Hold, string? While);
