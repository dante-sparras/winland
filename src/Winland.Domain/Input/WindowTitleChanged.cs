using System.Globalization;

namespace Winland.Domain;

/// <summary>A window's title changed.</summary>
public sealed record WindowTitleChanged(WindowId Window, string Title) : Event;
