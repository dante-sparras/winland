using System.Globalization;

namespace Winland.Domain;

public sealed record PointerButtonPressed(string Button) : Event
{
    public Point Position { get; init; }
}
