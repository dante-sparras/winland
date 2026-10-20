namespace Winland.PluginApi;

/// <summary>A box in desktop pixels. The width and height may be fractional; Winland rounds the edges.</summary>
public readonly record struct PluginLayoutBox(double X, double Y, double Width, double Height);

/// <summary>A tiled window a plugin layout places, and the box it has now, before gaps and the border.</summary>
public readonly record struct PluginLayoutWindow(long Address, PluginLayoutBox Box);

/// <summary>What a plugin layout is asked to arrange: the work area and the tiled windows in the layout's order.</summary>
public sealed class PluginLayoutRequest
{
    public PluginLayoutRequest(PluginLayoutBox area, IReadOnlyList<PluginLayoutWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        Area = area;
        Windows = windows;
    }

    public PluginLayoutBox Area { get; }

    public IReadOnlyList<PluginLayoutWindow> Windows { get; }
}

/// <summary>
/// One workspace's tiled layout. The host calls it on the plugin's worker, one call at a time.
/// <see cref="Recalculate"/> places the windows. <see cref="WindowAdded"/> and <see cref="WindowRemoved"/> report the
/// windows that joined or left since the previous call. <see cref="LayoutMessage"/> takes a <c>layoutmsg</c>.
/// </summary>
public abstract class PluginLayout
{
    public virtual void WindowAdded(PluginLayoutWindow window)
    {
    }

    public virtual void WindowRemoved(PluginLayoutWindow window)
    {
    }

    /// <summary>
    /// Places <see cref="PluginLayoutRequest.Windows"/>. The box at index <c>i</c> is where window <c>i</c> goes.
    /// A window past the end of the list keeps its box. Null asks Winland to use the grid.
    /// </summary>
    public abstract IReadOnlyList<PluginLayoutBox>? Recalculate(PluginLayoutRequest request);

    /// <summary>
    /// A layout message. Null accepts it. A string rejects it and is logged. Winland lays the windows out again either way.
    /// </summary>
    public virtual string? LayoutMessage(PluginLayoutRequest request, string message) => null;
}
