using System.Globalization;

namespace Winland.Domain;

public sealed record PointerButtonReleased(string Button) : Event
{
    public Point Position { get; init; }
}
