namespace Winland.Core;

/// <summary>
/// The Workspace a window is on, kept on the window itself as one number (ADR 0078). It outlives the desktop that wrote it
/// and goes with the window, so a desktop that starts reads where each window belongs from the windows and from no file.
/// This is what is written there and what a reading of it means.
/// </summary>
public static class WorkspaceMark
{
    /// <summary>The name of the window property that holds the mark.</summary>
    public const string Property = "Winland.Workspace";

    /// <summary>A window that carries no mark. A property of zero reads the same as none, so zero is never written: the property is removed.</summary>
    public const int None = 0;

    /// <summary>The mark of a window in the Scratchpad. It is above every Workspace number, so it names no Workspace.</summary>
    public const int Scratchpad = 99;

    /// <summary>True when <paramref name="mark"/> names a Workspace.</summary>
    /// <param name="mark">A mark, as a window carries it.</param>
    /// <returns>False for <see cref="None"/>, for <see cref="Scratchpad"/>, and for a number no Chord reaches.</returns>
    public static bool IsWorkspace(int mark) => mark >= 1 && mark <= WorkspaceBook.Maximum;

    /// <summary>What a window carries, from the number its property holds.</summary>
    /// <param name="property">The property as Windows returns it, the size of a pointer. Zero when the window has none.</param>
    /// <returns>
    /// A Workspace number, <see cref="Scratchpad"/>, or <see cref="None"/>. Any program can set the property,
    /// so a number Winoma never writes is no mark.
    /// </returns>
    public static int Read(long property) =>
        property == Scratchpad || (property >= 1 && property <= WorkspaceBook.Maximum) ? (int)property : None;
}
