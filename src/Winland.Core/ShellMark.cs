namespace Winland.Core;

/// <summary>
/// The mark on a window that is not a tile: a Winshell surface, or a group strip Winland drew.
/// Winland does not tile a tagged window and does not hide it when a Workspace switches (ADR 0176).
/// </summary>
public static class ShellMark
{
    /// <summary>The window property that carries the mark.</summary>
    public const string Property = "Winshell.Window";

    /// <summary>The value written on a tagged window. Any other value is no mark.</summary>
    public const int Value = 1;
}
