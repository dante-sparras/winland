using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// The fingers of a touchpad swipe moved. The delta is in pixels since the last update, right and down positive.
/// </summary>
public sealed record SwipeUpdated(int Fingers, double DeltaX, double DeltaY) : Event
{
    /// <summary>When the update happened, in milliseconds of a steady clock, or 0 when it is not known.</summary>
    public long TimeMs { get; init; }
}
