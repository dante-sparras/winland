using System.Globalization;

namespace Winland.Domain;

/// <summary>The armed long press has reached its delay. Matching reads the session and takes no lock.</summary>
public sealed record LongPressElapsed : Event;
