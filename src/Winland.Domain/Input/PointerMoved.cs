using System.Globalization;

namespace Winland.Domain;

public sealed record PointerMoved(Point Position) : Event
{
    public long? TimeMs { get; init; }
}
