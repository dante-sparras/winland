namespace Winland.Core;

/// <summary>
/// The Layout of a window's Workspace and the Group the window is in, kept on the window itself as one number (ADR 0050).
/// A window already carries its Workspace (ADR 0078), so a desktop that starts reads the Layout and each Group from the windows too,
/// and from no file: a desktop that was killed could not write one. This is what is written there and what a reading of it means.
/// </summary>
public static class TilingMark
{
    /// <summary>The name of the window property that holds the mark.</summary>
    public const string Property = "Winland.Tiling";

    /// <summary>A window that carries no mark: a plain tile on a Workspace in the dwindle Layout. A property of zero reads the same as none, so zero is never written.</summary>
    public const int None = 0;

    /// <summary>The most Groups on one Workspace, and the most windows in one Group, that a mark can name.</summary>
    public const int Largest = 255;

    private const int ScrollingBit = 1;
    private const int ShownBit = 2;
    private const int GroupShift = 8;
    private const int PositionShift = 16;
    private const int Mask = ScrollingBit | ShownBit | (0xFF << GroupShift) | (0xFF << PositionShift);

    /// <summary>What a mark says about a window.</summary>
    /// <param name="Scrolling">True when the window's Workspace is in the scrolling Layout.</param>
    /// <param name="Group">The number of the Group the window is in, from 1, unique on its Workspace. Zero when it is a plain tile.</param>
    /// <param name="Position">The window's place in its Group, from 0.</param>
    /// <param name="Shown">True when the Group shows this window.</param>
    public readonly record struct Reading(bool Scrolling, int Group, int Position, bool Shown);

    /// <summary>The number a window carries for what <see cref="Reading"/> says.</summary>
    /// <param name="scrolling">True when the window's Workspace is in the scrolling Layout.</param>
    /// <param name="group">The Group's number, 1 to <see cref="Largest"/>, or zero for a plain tile.</param>
    /// <param name="position">The window's place in the Group, 0 to <see cref="Largest"/> - 1. Ignored for a plain tile.</param>
    /// <param name="shown">True when the Group shows the window. Ignored for a plain tile.</param>
    /// <returns><see cref="None"/> for a plain tile in the dwindle Layout.</returns>
    public static int Write(bool scrolling, int group = 0, int position = 0, bool shown = false)
    {
        var mark = scrolling ? ScrollingBit : 0;
        if (group >= 1 && group <= Largest && position >= 0 && position < Largest)
        {
            mark |= (group << GroupShift) | ((position + 1) << PositionShift) | (shown ? ShownBit : 0);
        }

        return mark;
    }

    /// <summary>What a window carries, from the number its property holds.</summary>
    /// <param name="property">The property as Windows returns it, the size of a pointer. Zero when the window has none.</param>
    /// <returns>
    /// What the mark says. Any program can set the property, so a number Winoma never writes is no mark:
    /// bits it does not use, a place with no Group, or a Group with no place.
    /// </returns>
    public static Reading Read(long property)
    {
        if (property <= 0 || (property & ~(long)Mask) != 0)
        {
            return default;
        }

        var group = (int)((property >> GroupShift) & 0xFF);
        var place = (int)((property >> PositionShift) & 0xFF);
        var shown = (property & ShownBit) != 0;
        if ((group == 0) != (place == 0) || (group == 0 && shown))
        {
            return default;
        }

        return new Reading((property & ScrollingBit) != 0, group, place == 0 ? 0 : place - 1, shown);
    }
}
