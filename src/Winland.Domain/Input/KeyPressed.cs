using System.Globalization;

namespace Winland.Domain;

public sealed record KeyPressed(string Key) : Event
{
    public int ScanCode { get; init; }

    /// <summary>The keyboard raw input names for this press, when it could be attributed. Null when it could not.</summary>
    public string? Device { get; init; }
}
