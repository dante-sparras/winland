namespace Winland.Core.Tiling;

/// <summary>
/// A window carried by Super and the left button, from anywhere on the window, so a window without a title bar can move:
/// a tile, a floated window, or a Sticky one. The window follows the pointer. A release past <see cref="TileReturn.DragSlop"/>
/// drops a tile where the pointer is and leaves a floated or Sticky window where it was let go,
/// and a shorter one leaves the layout alone. One thread owns an instance: the placer's.
/// </summary>
public sealed class TileCarry
{
    private TileRect _from;
    private int _originX;
    private int _originY;

    /// <summary>The window being carried, or null.</summary>
    public long? Id { get; private set; }

    /// <summary>True when the window pressed last is floated or Sticky, so a drop leaves it where it was let go.</summary>
    public bool Floated { get; private set; }

    /// <summary>True when a Super press at the pointer should carry the window Windows draws there.</summary>
    /// <param name="superHeld">True while a Super finger is held.</param>
    /// <param name="tile">The tile under the pointer, from <see cref="TileHit.At"/>.</param>
    /// <param name="drawn">The top-level window Windows draws at the pointer.</param>
    /// <param name="floated">True when <paramref name="drawn"/> is a floated or a Sticky window, which is carried wherever it is.</param>
    /// <returns>False when a dialog, a panel, or another window that is neither covers the tile there.</returns>
    public static bool Claims(bool superHeld, long? tile, long? drawn, bool floated = false) =>
        superHeld && drawn is long id && (floated || tile == id);

    /// <summary>The left button went down on <paramref name="id"/>.</summary>
    /// <param name="id">The window.</param>
    /// <param name="x">Pointer x, in pixels.</param>
    /// <param name="y">Pointer y, in pixels.</param>
    /// <param name="from">Where the window is drawn now.</param>
    /// <param name="floated">True when the window is floated or Sticky.</param>
    public void Press(long id, int x, int y, TileRect from, bool floated = false)
    {
        Id = id;
        Floated = floated;
        _from = from;
        _originX = x;
        _originY = y;
    }

    /// <summary>Where to draw the carried window for the pointer at <paramref name="x"/>, <paramref name="y"/>.</summary>
    /// <param name="x">Pointer x, in pixels.</param>
    /// <param name="y">Pointer y, in pixels.</param>
    /// <returns>The window's rectangle moved by the pointer's travel, or null when nothing is carried.</returns>
    public TileRect? Follow(int x, int y) =>
        Id is null ? null : _from with { X = _from.X + x - _originX, Y = _from.Y + y - _originY };

    /// <summary>The left button came up. Nothing is carried afterwards.</summary>
    /// <param name="x">Pointer x, in pixels.</param>
    /// <param name="y">Pointer y, in pixels.</param>
    /// <returns>
    /// The drop for the shell, with the rectangle the window was carried to, or null when the pointer did not travel past <see cref="TileReturn.DragSlop"/>.
    /// </returns>
    public TileDrag? Release(int x, int y)
    {
        if (Id is not long id)
        {
            return null;
        }

        var to = Follow(x, y);
        Id = null;
        var carried = Math.Abs(x - _originX) > TileReturn.DragSlop || Math.Abs(y - _originY) > TileReturn.DragSlop;
        return carried ? new TileDrag(id, Drop: true, x, y, 0, 0, to) : null;
    }
}
