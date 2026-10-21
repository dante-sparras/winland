using System.Globalization;

namespace Winland.Domain;

/// <summary>The system finished resizing a window by its border. <see cref="Bounds"/> is the visible frame it was left at.</summary>
public sealed record WindowSizeEnded(WindowId Window, Point Pointer, Rectangle? Bounds = null) : Event;
