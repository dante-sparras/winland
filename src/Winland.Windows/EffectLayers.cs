using System.Collections.Concurrent;

namespace Winland.Windows;

/// <summary>
/// The border, shadow and glow each Client has now, which a stand-in that draws effects paints into its frames. Any thread
/// may read it.
/// </summary>
internal static class EffectLayers
{
    private static readonly ConcurrentDictionary<WindowId, (BorderRing? Ring, DropShadow? Shadow, InnerGlow? Glow)> Layers = new();

    /// <summary>Replaces every Client's layers.</summary>
    internal static void Update(
        IReadOnlyDictionary<WindowId, BorderRing> rings,
        IReadOnlyDictionary<WindowId, DropShadow> shadows,
        IReadOnlyDictionary<WindowId, InnerGlow> glows)
    {
        var windows = new HashSet<WindowId>(rings.Keys);
        windows.UnionWith(shadows.Keys);
        windows.UnionWith(glows.Keys);
        foreach (var window in Layers.Keys)
        {
            if (!windows.Contains(window))
                Layers.TryRemove(window, out _);
        }

        foreach (var window in windows)
            Layers[window] = (rings.GetValueOrDefault(window), shadows.GetValueOrDefault(window), glows.GetValueOrDefault(window));
    }

    internal static (BorderRing? Ring, DropShadow? Shadow, InnerGlow? Glow) Of(WindowId window) =>
        Layers.TryGetValue(window, out var layers) ? layers : default;

    internal static void Clear() => Layers.Clear();
}
