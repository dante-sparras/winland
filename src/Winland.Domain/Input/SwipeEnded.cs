using System.Globalization;

namespace Winland.Domain;

/// <summary>The swipe ended. A cancelled swipe ended because the finger count changed, not because the fingers lifted.</summary>
public sealed record SwipeEnded(bool Cancelled = false) : Event
{
    /// <summary>When the swipe ended, in milliseconds of a steady clock, or 0 when it is not known.</summary>
    public long TimeMs { get; init; }
}
