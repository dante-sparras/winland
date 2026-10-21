using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// The full set of attached monitors, in adapter order. A monitor absent from the next event was unplugged.
/// Each monitor's rectangle and <see cref="Monitor.WorkArea"/> are physical pixels, and <see cref="Monitor.Scale"/>
/// is its DPI divided by 96.
/// </summary>
public sealed record MonitorsChanged(IReadOnlyList<Monitor> Monitors) : Event;
