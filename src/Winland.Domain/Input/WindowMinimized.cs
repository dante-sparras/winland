using System.Globalization;

namespace Winland.Domain;

public sealed record WindowMinimized(WindowId Window) : Event;
