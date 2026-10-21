using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// The system finished moving a window by its title bar. <see cref="Bounds"/> is the visible frame the window was
/// dropped at, which is where its motion into the Layout starts. Without it the motion starts at the lifted tile.
/// </summary>
public sealed record WindowMoveEnded(WindowId Window, Point Pointer, Rectangle? Bounds = null) : Event;
