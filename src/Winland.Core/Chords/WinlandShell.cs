namespace Winland.Core.Chords;

/// <summary>
/// Maps the window half of <see cref="ShellCommand"/> onto <see cref="WinlandCommand"/> and back.
/// Host actions, app roles, panels, and plugin commands stay on the shell side and map to nothing here.
/// </summary>
public static class WinlandShell
{
    /// <summary>The Winland command <paramref name="command"/> names, or null when the shell runs it.</summary>
    /// <param name="command">A shell command.</param>
    /// <returns>The same command as Winland sees it, or null.</returns>
    public static WinlandCommand? ToWinland(ShellCommand command) => command switch
    {
        ShellCommand.JumpWorkspace jump => new WinlandCommand.JumpWorkspace(jump.Number),
        ShellCommand.MoveWindow move => new WinlandCommand.MoveWindow(move.Number, move.Follow),
        ShellCommand.CycleWorkspace cycle => new WinlandCommand.CycleWorkspace(cycle.Cycle),
        ShellCommand.Focus focus => new WinlandCommand.Focus(focus.Direction),
        ShellCommand.Swap swap => new WinlandCommand.Swap(swap.Direction),
        ShellCommand.Resize resize => new WinlandCommand.Resize(resize.X, resize.Y),
        ShellCommand.GroupStep step => new WinlandCommand.GroupStep(step.Delta),
        ShellCommand.GroupIndex index => new WinlandCommand.GroupIndex(index.Index),
        ShellCommand.JoinGroup join => new WinlandCommand.JoinGroup(join.Direction),
        ShellCommand.MoveMonitor monitor => new WinlandCommand.MoveMonitor(monitor.Direction),
        ShellCommand.Window window => new WinlandCommand.Window(window.Action),
        ShellCommand.Nothing => new WinlandCommand.Nothing(),
        ShellCommand.Host or ShellCommand.AppRole or ShellCommand.OpenPanel or ShellCommand.OpenRightPanel or ShellCommand.PluginCommand => null,
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    /// <summary>The shell command <paramref name="command"/> names. The shell still executes it.</summary>
    /// <param name="command">A Winland command.</param>
    /// <returns>The same command as the shell sees it.</returns>
    public static ShellCommand ToShell(WinlandCommand command) => command switch
    {
        WinlandCommand.JumpWorkspace jump => new ShellCommand.JumpWorkspace(jump.Number),
        WinlandCommand.MoveWindow move => new ShellCommand.MoveWindow(move.Number, move.Follow),
        WinlandCommand.CycleWorkspace cycle => new ShellCommand.CycleWorkspace(cycle.Cycle),
        WinlandCommand.Focus focus => new ShellCommand.Focus(focus.Direction),
        WinlandCommand.Swap swap => new ShellCommand.Swap(swap.Direction),
        WinlandCommand.Resize resize => new ShellCommand.Resize(resize.X, resize.Y),
        WinlandCommand.GroupStep step => new ShellCommand.GroupStep(step.Delta),
        WinlandCommand.GroupIndex index => new ShellCommand.GroupIndex(index.Index),
        WinlandCommand.JoinGroup join => new ShellCommand.JoinGroup(join.Direction),
        WinlandCommand.MoveMonitor monitor => new ShellCommand.MoveMonitor(monitor.Direction),
        WinlandCommand.Window window => new ShellCommand.Window(window.Action),
        WinlandCommand.Nothing => new ShellCommand.Nothing(),
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
