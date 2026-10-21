namespace Winland.Core;

/// <summary>
/// A Monitor the core tracks across hotplug, keyed by <see cref="Id"/>. <see cref="MonitorSnapshot"/> is
/// the snapshot of it that <see cref="State"/> reports.
/// </summary>
/// <remarks>
/// <see cref="Reported"/> is the display as Windows reported it, and <see cref="Effective"/> is the same
/// display after the <c>monitor</c> rules are applied.
/// </remarks>
internal sealed class LiveMonitor(MonitorId id, Monitor reported, Monitor effective)
{
    public MonitorId Id { get; } = id;

    public Monitor Reported { get; set; } = reported;

    /// <summary>The Monitor as the monitor rules arrange it.</summary>
    public Monitor Arranged { get; set; } = effective;

    /// <summary>The Monitor as Winland uses it, after the monitor rules.</summary>
    public Monitor Effective => Arranged;

    public WorkspaceId? Active { get; set; }

    public WorkspaceId? Special { get; set; }

    public bool Focused { get; set; }

    /// <summary>The effective rectangle of the whole display.</summary>
    public Rectangle Bounds => new(Effective.X, Effective.Y, Effective.Width, Effective.Height);

    /// <summary>Whether the display as Windows reported it contains <paramref name="point"/>.</summary>
    public bool Covers(Point point) =>
        point.X >= Reported.X
        && point.Y >= Reported.Y
        && point.X < Reported.X + Reported.Width
        && point.Y < Reported.Y + Reported.Height;
}
