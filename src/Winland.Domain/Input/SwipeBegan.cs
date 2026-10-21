using System.Globalization;

namespace Winland.Domain;

/// <summary>Fingers came down on a touchpad together. Movement follows as <see cref="SwipeUpdated"/>.</summary>
public sealed record SwipeBegan(int Fingers) : Event;
