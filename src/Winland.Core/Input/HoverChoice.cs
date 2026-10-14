namespace Winland.Core.Input;

/// <summary>What is under the pointer when it moves.</summary>
/// <param name="Tile">The tile whose place holds the pointer, or null.</param>
/// <param name="Drawn">The top-level window drawn at the pointer, or null. A window above the tile, such as the snipping overlay, is this one.</param>
/// <param name="Focused">The tile that has the keyboard on this workspace, or null.</param>
/// <param name="ButtonHeld">True while a mouse button is down.</param>
/// <param name="SurfaceHeld">True while an Winoma panel has the keyboard.</param>
public readonly record struct HoverFacts(long? Tile, long? Drawn, long? Focused, bool ButtonHeld, bool SurfaceHeld);

/// <summary>Hover focus: the pointer moving onto a tile gives that tile the keyboard.</summary>
public static class HoverChoice
{
    /// <summary>The tile to focus, or null when the keyboard stays where it is.</summary>
    /// <param name="facts">What is under the pointer.</param>
    /// <returns>A tile id, or null.</returns>
    public static long? Target(HoverFacts facts)
    {
        if (facts.Tile is not long tile || facts.ButtonHeld || facts.SurfaceHeld)
        {
            return null;
        }

        if (facts.Drawn != tile || facts.Focused == tile)
        {
            return null;
        }

        return tile;
    }
}
