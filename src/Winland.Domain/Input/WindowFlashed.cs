using System.Globalization;

namespace Winland.Domain;

/// <summary>The window flashed for attention without taking the foreground.</summary>
public sealed record WindowFlashed(WindowId Window) : Event;
