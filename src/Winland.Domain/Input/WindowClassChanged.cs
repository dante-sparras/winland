using System.Globalization;

namespace Winland.Domain;

/// <summary>A window's class changed.</summary>
public sealed record WindowClassChanged(WindowId Window, string Class) : Event;
