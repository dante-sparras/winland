namespace Winland.Core.Binds;

/// <summary>What Winland does when a bind names its own dispatcher.</summary>
public abstract record WinlandCommand
{
    /// <summary>Jump to a workspace, creating it when needed.</summary>
    /// <param name="Number">The workspace number, from 1.</param>
    public sealed record JumpWorkspace(int Number) : WinlandCommand;

    /// <summary>Send the focused window to a workspace.</summary>
    /// <param name="Number">The workspace number, from 1.</param>
    /// <param name="Follow">True when the keyboard follows the window.</param>
    public sealed record MoveWindow(int Number, bool Follow) : WinlandCommand;

    /// <summary>Next, previous, or former workspace.</summary>
    /// <param name="Cycle">Which way to go.</param>
    public sealed record CycleWorkspace(WorkspaceCycle Cycle) : WinlandCommand;

    /// <summary>Move focus.</summary>
    /// <param name="Direction">Where focus goes.</param>
    public sealed record Focus(Direction Direction) : WinlandCommand;

    /// <summary>Swap with the neighbor.</summary>
    /// <param name="Direction">Which neighbor.</param>
    public sealed record Swap(Direction Direction) : WinlandCommand;

    /// <summary>Resize the focused window by a pixel delta. Negative X expands the left edge. Negative Y shrinks from the top.</summary>
    /// <param name="X">Horizontal pixels. Negative expands the left edge.</param>
    /// <param name="Y">Vertical pixels. Negative shrinks from the top.</param>
    public sealed record Resize(int X, int Y) : WinlandCommand;

    /// <summary>Cycle the focused group by <paramref name="Delta"/> members.</summary>
    /// <param name="Delta">How many members to step. Negative steps backward.</param>
    public sealed record GroupStep(int Delta) : WinlandCommand;

    /// <summary>Show the 1-based member of the focused group.</summary>
    /// <param name="Index">The member, from 1.</param>
    public sealed record GroupIndex(int Index) : WinlandCommand;

    /// <summary>Pull the focused window into the group in this direction.</summary>
    /// <param name="Direction">Which neighbor to join.</param>
    public sealed record JoinGroup(Direction Direction) : WinlandCommand;

    /// <summary>Move the current workspace onto the monitor in this direction.</summary>
    /// <param name="Direction">Which monitor.</param>
    public sealed record MoveMonitor(Direction Direction) : WinlandCommand;

    /// <summary>A command with no payload that Winland runs on the windows of the current workspace.</summary>
    /// <param name="Action">Which command.</param>
    public sealed record Window(WindowAction Action) : WinlandCommand;

    /// <summary>Do nothing. A bind to it keeps its key from the program that would act on it otherwise.</summary>
    public sealed record Nothing : WinlandCommand;
}
