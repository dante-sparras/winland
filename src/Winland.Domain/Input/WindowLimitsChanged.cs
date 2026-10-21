using System.Globalization;

namespace Winland.Domain;

/// <summary>The window's minimum or maximum track size changed. A null size is no limit.</summary>
public sealed record WindowLimitsChanged(WindowId Window, Size? MinSize, Size? MaxSize) : Event;
