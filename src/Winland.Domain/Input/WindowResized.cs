using System.Globalization;

namespace Winland.Domain;

/// <summary>A Client moved or resized itself. <see cref="Bounds"/> is its visible frame.</summary>
public sealed record WindowResized(WindowId Window, Rectangle Bounds) : Event;
