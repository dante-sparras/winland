using System.Globalization;

namespace Winland.Domain;

public sealed record WindowClosed(WindowId Window) : Event;
