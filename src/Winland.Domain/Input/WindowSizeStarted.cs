using System.Globalization;

namespace Winland.Domain;

/// <summary>The system began resizing a window by its border. <see cref="Pointer"/> is where the pointer is.</summary>
public sealed record WindowSizeStarted(WindowId Window, Point Pointer) : Event;
