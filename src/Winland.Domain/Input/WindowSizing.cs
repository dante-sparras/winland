using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// The border of a window the system is resizing moved. <see cref="Pointer"/> is where the pointer is, and
/// <see cref="Bounds"/> the visible frame now.
/// </summary>
public sealed record WindowSizing(WindowId Window, Point Pointer, Rectangle? Bounds = null) : Event;
