using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// A <c>monitor</c> line that only reserves space. A name with no reserved area matches and changes nothing.
/// Mode, position, scale, transform, mirror, color, and <c>disable</c> are not rules.
/// </summary>
internal readonly record struct MonitorRule(
    string Selector,
    int ReservedTop,
    int ReservedBottom,
    int ReservedLeft,
    int ReservedRight,
    bool HasReserved);
