using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// A Client's frame is not where its Layout put it, soon after the core changed it. Electron windows restore their
/// own bounds just after they leave the system's maximized state. <see cref="Bounds"/> is the visible frame.
/// </summary>
public sealed record WindowDrifted(WindowId Window, Rectangle Bounds) : Event;
