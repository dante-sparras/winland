namespace Winland.Windows;

/// <summary>
/// Draws each Client's inner glow, as the pinned release draws it, when the config turns it on. The glow is four
/// click-through layered strips along the inside of the window's edge, kept just above the Client in the z-order, so the
/// middle of the window is not covered.
/// </summary>
internal static class GlowHost
{
    private static readonly GradientLayerHost<InnerGlow> Host = new(
        "WinlandGlow",
        "Winland glows",
        0x8000 + 0x4D,
        below: false,
        GlowPainter.Layout,
        GlowPainter.Paint);

    /// <summary>Replaces every glow with <paramref name="glows"/>.</summary>
    internal static void Update(IReadOnlyDictionary<WindowId, InnerGlow> glows) => Host.Update(glows);

    /// <summary>The window moved, resized, showed or hid, so its glow follows.</summary>
    internal static void Follow(WindowId window) => Host.Follow(window);

    /// <summary>Removes every glow and ends the thread.</summary>
    internal static void Stop() => Host.Stop();
}
