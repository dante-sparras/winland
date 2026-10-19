using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace Winland.Windows;

/// <summary>
/// Changes a display-config path list so one display shows another's picture, and back. Windows clones two displays
/// on one graphics adapter by giving both paths the same source, as Microsoft's display-config example does. The
/// methods only edit the arrays; <see cref="Win32DisplayTopology"/> hands them to Windows.
/// </summary>
internal static class DisplayTopology
{
    /// <summary>A mode index that names no mode.</summary>
    public const uint NoMode = 0xFFFFFFFF;

    public static long Key(LUID adapter) => ((long)adapter.HighPart << 32) | adapter.LowPart;

    public static LUID Luid(long key) => new() { HighPart = (int)(key >> 32), LowPart = (uint)key };

    /// <summary>The index of the path that shows on that target, or -1.</summary>
    public static int FindTarget(ReadOnlySpan<DISPLAYCONFIG_PATH_INFO> paths, long adapter, uint target)
    {
        for (var i = 0; i < paths.Length; i++)
        {
            if (Key(paths[i].targetInfo.adapterId) == adapter && paths[i].targetInfo.id == target)
                return i;
        }

        return -1;
    }

    /// <summary>Whether another path shows the same source as the path at <paramref name="index"/>.</summary>
    public static bool Shares(ReadOnlySpan<DISPLAYCONFIG_PATH_INFO> paths, int index)
    {
        for (var i = 0; i < paths.Length; i++)
        {
            if (i != index && SameSource(paths[i], paths[index]))
                return true;
        }

        return false;
    }

    /// <summary>The source mode the path shows, or null when it names none.</summary>
    public static DisplaySourceMode? SourceMode(DISPLAYCONFIG_PATH_INFO path, ReadOnlySpan<DISPLAYCONFIG_MODE_INFO> modes)
    {
        var index = path.sourceInfo.Anonymous.modeInfoIdx;
        if (index >= modes.Length || modes[(int)index].infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            return null;

        var mode = modes[(int)index].Anonymous.sourceMode;
        return new DisplaySourceMode(mode.width, mode.height, (uint)mode.pixelFormat, mode.position.x, mode.position.y);
    }

    /// <summary>
    /// Makes the path at <paramref name="mirror"/> show the source of the path at <paramref name="source"/>. Returns
    /// null, or why it cannot: the two must be different displays on one graphics adapter. The mirror's own modes are
    /// dropped, so Windows picks a mode both displays can show.
    /// </summary>
    public static string? Clone(Span<DISPLAYCONFIG_PATH_INFO> paths, int mirror, int source)
    {
        if (mirror == source || SameSource(paths[mirror], paths[source]))
            return "it shows that picture already.";

        if (Key(paths[mirror].sourceInfo.adapterId) != Key(paths[source].sourceInfo.adapterId))
            return "the two displays are on different graphics adapters.";

        paths[mirror].sourceInfo.id = paths[source].sourceInfo.id;
        paths[mirror].sourceInfo.Anonymous.modeInfoIdx = NoMode;
        paths[mirror].targetInfo.Anonymous.modeInfoIdx = NoMode;
        paths[source].targetInfo.Anonymous.modeInfoIdx = NoMode;
        return null;
    }

    /// <summary>
    /// Gives the path at <paramref name="index"/> back the source it showed, at the mode it had there, appended to
    /// <paramref name="modes"/>. Windows picks the target mode again.
    /// </summary>
    public static void Separate(Span<DISPLAYCONFIG_PATH_INFO> paths, List<DISPLAYCONFIG_MODE_INFO> modes, int index, DisplayMirrorOriginal original)
    {
        ArgumentNullException.ThrowIfNull(modes);
        ArgumentNullException.ThrowIfNull(original);
        paths[index].sourceInfo.id = original.SourceId;
        paths[index].targetInfo.Anonymous.modeInfoIdx = NoMode;
        paths[index].sourceInfo.Anonymous.modeInfoIdx = NoMode;
        if (original.Mode is not DisplaySourceMode mode)
            return;

        var info = new DISPLAYCONFIG_MODE_INFO
        {
            infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE,
            id = original.SourceId,
            adapterId = paths[index].sourceInfo.adapterId,
        };
        info.Anonymous.sourceMode.width = mode.Width;
        info.Anonymous.sourceMode.height = mode.Height;
        info.Anonymous.sourceMode.pixelFormat = (DISPLAYCONFIG_PIXELFORMAT)mode.PixelFormat;
        info.Anonymous.sourceMode.position.x = mode.X;
        info.Anonymous.sourceMode.position.y = mode.Y;
        modes.Add(info);
        paths[index].sourceInfo.Anonymous.modeInfoIdx = (uint)(modes.Count - 1);
    }

    /// <summary>The modes some path still names, in order, with the paths' indexes moved to match.</summary>
    public static DISPLAYCONFIG_MODE_INFO[] Compact(Span<DISPLAYCONFIG_PATH_INFO> paths, IReadOnlyList<DISPLAYCONFIG_MODE_INFO> modes)
    {
        ArgumentNullException.ThrowIfNull(modes);
        var moved = new uint[modes.Count];
        Array.Fill(moved, NoMode);
        var kept = new List<DISPLAYCONFIG_MODE_INFO>(modes.Count);
        for (var i = 0; i < paths.Length; i++)
        {
            paths[i].sourceInfo.Anonymous.modeInfoIdx = Keep(paths[i].sourceInfo.Anonymous.modeInfoIdx);
            paths[i].targetInfo.Anonymous.modeInfoIdx = Keep(paths[i].targetInfo.Anonymous.modeInfoIdx);
        }

        return [.. kept];

        uint Keep(uint index)
        {
            if (index >= modes.Count)
                return NoMode;

            if (moved[index] == NoMode)
            {
                moved[index] = (uint)kept.Count;
                kept.Add(modes[(int)index]);
            }

            return moved[index];
        }
    }

    private static bool SameSource(DISPLAYCONFIG_PATH_INFO one, DISPLAYCONFIG_PATH_INFO other) =>
        Key(one.sourceInfo.adapterId) == Key(other.sourceInfo.adapterId) && one.sourceInfo.id == other.sourceInfo.id;
}

/// <summary>Mirrors displays with the system's display configuration. The change is not saved, so Windows' own layout stays as it was.</summary>
// Stryker disable once all : changes the real displays; what to change is decided and tested in DisplayTopology.
internal sealed class Win32DisplayTopology : IDisplayTopology
{
    private const SET_DISPLAY_CONFIG_FLAGS Supplied =
        SET_DISPLAY_CONFIG_FLAGS.SDC_USE_SUPPLIED_DISPLAY_CONFIG | SET_DISPLAY_CONFIG_FLAGS.SDC_ALLOW_CHANGES;

    public DisplayMirrorOriginal? Mirror(DisplayMirrorRequest request, out string failure)
    {
        if (!MonitorAdapter.TryConfig(out var paths, out var modes))
        {
            failure = "Windows did not report the displays.";
            return null;
        }

        var mirror = Named(paths, request.Device);
        var source = Named(paths, request.Source);
        if (mirror < 0 || source < 0)
        {
            failure = "Windows does not show both displays.";
            return null;
        }

        if (DisplayTopology.Shares(paths, mirror))
        {
            failure = "it shows the same picture as another display already.";
            return null;
        }

        var original = new DisplayMirrorOriginal(
            request.Device,
            request.Source,
            MonitorAdapter.ReadTarget(paths[mirror]).Description,
            DisplayTopology.Key(paths[mirror].targetInfo.adapterId),
            paths[mirror].targetInfo.id,
            paths[mirror].sourceInfo.id,
            DisplayTopology.SourceMode(paths[mirror], modes));
        if (DisplayTopology.Clone(paths, mirror, source) is string refused)
        {
            failure = refused;
            return null;
        }

        if (!Commit(paths, DisplayTopology.Compact(paths, modes)))
        {
            failure = "Windows cannot show the two displays as one.";
            return null;
        }

        failure = "";
        return original;
    }

    public bool Unmirror(DisplayMirrorOriginal original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (!MonitorAdapter.TryConfig(out var paths, out var modes))
            return false;

        var index = DisplayTopology.FindTarget(paths, original.Adapter, original.Target);
        if (index < 0 || !DisplayTopology.Shares(paths, index))
            return true;

        var list = new List<DISPLAYCONFIG_MODE_INFO>(modes);
        DisplayTopology.Separate(paths, list, index, original);
        return Commit(paths, DisplayTopology.Compact(paths, list));
    }

    private static int Named(DISPLAYCONFIG_PATH_INFO[] paths, string device)
    {
        for (var i = 0; i < paths.Length; i++)
        {
            if (string.Equals(MonitorAdapter.SourceName(paths[i]), device, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    // Windows checks the layout before it shows it, so one it refuses changes nothing.
    private static bool Commit(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes) =>
        PInvoke.SetDisplayConfig(paths, modes, Supplied | SET_DISPLAY_CONFIG_FLAGS.SDC_VALIDATE) == 0
        && PInvoke.SetDisplayConfig(paths, modes, Supplied | SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY) == 0;
}
