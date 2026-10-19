namespace Winland;

/// <summary>A request that the display <see cref="Device"/> show the same picture as the display <see cref="Source"/>.</summary>
public readonly record struct DisplayMirrorRequest(string Device, string Source);

/// <summary>The desktop area a display-config source showed: its size in pixels, its pixel format and its position.</summary>
public readonly record struct DisplaySourceMode(uint Width, uint Height, uint PixelFormat, int X, int Y);

/// <summary>
/// A display Winland made show another one's picture, and what it showed before, so it can be put back. Windows folds
/// a mirrored display into its source, so <see cref="Device"/> and <see cref="Description"/> are the name and the
/// friendly name it had before. <see cref="Adapter"/> and <see cref="Target"/> are its display-config target, and
/// <see cref="SourceId"/> and <see cref="Mode"/> the source it showed. <see cref="Mode"/> is null when Windows reported
/// no source mode for it.
/// </summary>
public sealed record DisplayMirrorOriginal(
    string Device,
    string Source,
    string Description,
    long Adapter,
    uint Target,
    uint SourceId,
    DisplaySourceMode? Mode);

/// <summary>How the displays Winland mirrors show up in the monitor list.</summary>
public static class DisplayMirrors
{
    /// <summary>
    /// The displays Windows reports, with each display Winland mirrors added back. Windows reports a mirrored display
    /// as part of its source, so it comes back under the name it had, with the source's rectangle, and
    /// <see cref="Monitor.MirrorOf"/> naming that source. A mirror whose source is gone, or whose own name Windows
    /// reports again, adds nothing.
    /// </summary>
    public static IReadOnlyList<Monitor> Report(IReadOnlyList<Monitor> reported, IReadOnlyList<DisplayMirrorOriginal> mirrors)
    {
        ArgumentNullException.ThrowIfNull(reported);
        ArgumentNullException.ThrowIfNull(mirrors);
        if (mirrors.Count == 0)
            return reported;

        var listed = new List<Monitor>(reported);
        foreach (var mirror in mirrors)
        {
            if (Named(listed, mirror.Device) is not null || Named(reported, mirror.Source) is not Monitor source)
                continue;

            listed.Add(source with { Name = mirror.Device, Description = mirror.Description, MirrorOf = source.Name });
        }

        return listed;
    }

    private static Monitor? Named(IReadOnlyList<Monitor> monitors, string name)
    {
        foreach (var monitor in monitors)
        {
            if (string.Equals(monitor.Name, name, StringComparison.OrdinalIgnoreCase))
                return monitor;
        }

        return null;
    }
}
