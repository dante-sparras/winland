using System.Globalization;

namespace Winland.Domain;

/// <summary>The system began moving a window by its title bar. <see cref="Pointer"/> is where the pointer is.</summary>
public sealed record WindowMoveStarted(WindowId Window, Point Pointer) : Event;
