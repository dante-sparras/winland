using System.Globalization;

namespace Winland.Domain;

/// <summary>An enabled Monitor a dispatcher can address.</summary>
internal readonly record struct MonitorTarget(
    MonitorId Id,
    string Name,
    string Description,
    int X,
    int Y,
    int Width,
    int Height);
