namespace Winland.Core.Tiling;

/// <summary>Dwindle keeps every tile visible. Scrolling lines tiles up past the edge of the screen.</summary>
public enum LayoutKind
{
    /// <summary>Every window on the workspace stays visible.</summary>
    Dwindle,

    /// <summary>Windows sit in a row that can extend past the screen.</summary>
    Scrolling,
}

/// <summary>A compass direction used for focus, swap, and resize.</summary>
public enum Direction
{
    /// <summary>Left.</summary>
    Left,

    /// <summary>Right.</summary>
    Right,

    /// <summary>Up.</summary>
    Up,

    /// <summary>Down.</summary>
    Down,
}

/// <summary>Where one window should be drawn, in pixels relative to the work area origin.</summary>
/// <param name="Id">Window identity.</param>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Visible">False when the window is an inactive group member.</param>
/// <param name="Raised">True when the window covers the others, as in fullscreen.</param>
/// <param name="Above">
/// True for a tile that is kept above the other tiles, as a full-width window is over the ones it covers.
/// It is still a tile: it keeps clear of the bar, and keeps its group's strip.
/// </param>
public readonly record struct Placement(long Id, int X, int Y, int Width, int Height, bool Visible, bool Raised = false, bool Above = false);

/// <summary>The strip along the top of a group's tile that shows the group's windows.</summary>
/// <param name="Rect">Where the strip is drawn, in pixels.</param>
/// <param name="Members">The group's windows, in the order Super+Alt+1 through 5 show them.</param>
/// <param name="Active">The index in <paramref name="Members"/> of the window shown.</param>
public readonly record struct GroupStrip(TileRect Rect, IReadOnlyList<long> Members, int Active);

/// <summary>The smallest size a window will accept, in pixels.</summary>
/// <param name="Id">Window identity.</param>
/// <param name="Width">Minimum width.</param>
/// <param name="Height">Minimum height.</param>
public readonly record struct WindowMinimum(long Id, int Width, int Height);

/// <summary>A Super-drag the shell should apply. <see cref="Drop"/> uses the pointer. Otherwise the pixel delta resizes.</summary>
/// <param name="Id">Window identity.</param>
/// <param name="Drop">True when the window should split the tile under the pointer.</param>
/// <param name="X">Pointer x.</param>
/// <param name="Y">Pointer y.</param>
/// <param name="Dx">Horizontal resize delta.</param>
/// <param name="Dy">Vertical resize delta.</param>
/// <param name="Rect">Where a Carry drew the window when it was let go. A floated or Sticky window stays there. Null for a resize.</param>
public readonly record struct TileDrag(long Id, bool Drop, int X, int Y, int Dx, int Dy, TileRect? Rect = null);

/// <summary>A pixel rectangle.</summary>
/// <param name="X">Left.</param>
/// <param name="Y">Top.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct TileRect(int X, int Y, int Width, int Height);
