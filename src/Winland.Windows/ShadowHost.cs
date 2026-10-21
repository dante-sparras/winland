namespace Winland.Windows;

/// <summary>
/// Draws each Client's shadow, as Winland draws it, when the config tuned it. The shadow is click-through
/// layered windows kept just below the Client in the z-order, around the window and not under it.
/// </summary>
internal static class ShadowHost
{
    private static readonly GradientLayerHost<DropShadow> Host = new(
        "WinlandShadow",
        "Winland shadows",
        0x8000 + 0x4C,
        below: true,
        ShadowPainter.Layout,
        ShadowPainter.Paint);

    /// <summary>Replaces every shadow with <paramref name="shadows"/>.</summary>
    internal static void Update(IReadOnlyDictionary<WindowId, DropShadow> shadows) => Host.Update(shadows);

    /// <summary>The window moved, resized, showed or hid, so its shadow follows.</summary>
    internal static void Follow(WindowId window) => Host.Follow(window);

    /// <summary>Removes every shadow and ends the thread.</summary>
    internal static void Stop() => Host.Stop();
}
