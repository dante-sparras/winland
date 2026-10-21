using System.Globalization;

namespace Winland.Domain;

public sealed record WindowRestored(WindowId Window) : Event;
