namespace Winland.Core.Workspaces;

/// <summary>
/// Which Workspace a desktop that starts puts in front. Nothing is read from a file: each window carries its Workspace (ADR 0078),
/// and the Workspace in front is the one whose windows are on the glass (ADR 0003).
/// After a replace only that Workspace's windows are on the glass. After a crash or a Stop every window is, so there is a tie to break.
/// </summary>
public static class WorkspaceFront
{
    /// <summary>
    /// The Workspace to put in front. The Workspace of the window that has the keyboard comes first: it is what the person was working in.
    /// Without one, the Workspace with the most windows on the glass, and the lowest number among equals.
    /// With no marked window on the glass, the lowest number that holds no window, so an empty Workspace in front is not kept across a restart.
    /// </summary>
    /// <param name="onGlass">How many marked windows each Workspace has on the glass. Indexed by Workspace number, and slot zero is not read.</param>
    /// <param name="held">How many windows each Workspace holds, on the glass or hidden. Indexed the same way.</param>
    /// <param name="keyboard">The Workspace of the window that has the keyboard, or zero when that window carries none or is not on the glass.</param>
    /// <returns>A Workspace number, 1 through <see cref="WorkspaceBook.Maximum"/>.</returns>
    public static int Pick(IReadOnlyList<int> onGlass, IReadOnlyList<int> held, int keyboard)
    {
        ArgumentNullException.ThrowIfNull(onGlass);
        ArgumentNullException.ThrowIfNull(held);
        if (keyboard >= 1 && keyboard <= WorkspaceBook.Maximum && At(onGlass, keyboard) > 0)
        {
            return keyboard;
        }

        var most = 0;
        var front = 0;
        for (var number = 1; number <= WorkspaceBook.Maximum; number++)
        {
            if (At(onGlass, number) > most)
            {
                most = At(onGlass, number);
                front = number;
            }
        }

        if (front > 0)
        {
            return front;
        }

        for (var number = 1; number <= WorkspaceBook.Maximum; number++)
        {
            if (At(held, number) == 0)
            {
                return number;
            }
        }

        // Every Workspace holds a hidden window and none is on the glass. The first is as good as any.
        return 1;
    }

    /// <summary>
    /// The Workspace to put in front on each monitor but the one <paramref name="keyboard"/> is on, where there are several:
    /// the one with the most windows on the glass there, and the lowest number among equals. A monitor that shows no window is given none,
    /// and takes the lowest free number (ADR 0079).
    /// </summary>
    /// <param name="onGlass">How many marked windows each Workspace has on the glass. Indexed by Workspace number, and slot zero is not read.</param>
    /// <param name="monitorOf">The monitor a Workspace is on, read from where its windows are.</param>
    /// <param name="keyboard">The Workspace <see cref="Pick"/> gave the keyboard. It is in front on its own monitor.</param>
    /// <returns>One Workspace for each other monitor that shows a window, in no order.</returns>
    public static IReadOnlyList<int> PerMonitor(IReadOnlyList<int> onGlass, Func<int, int> monitorOf, int keyboard)
    {
        ArgumentNullException.ThrowIfNull(onGlass);
        ArgumentNullException.ThrowIfNull(monitorOf);
        var taken = monitorOf(keyboard);
        var fronts = new List<int>();
        var most = new Dictionary<int, int>();
        for (var number = 1; number <= WorkspaceBook.Maximum; number++)
        {
            var count = At(onGlass, number);
            if (count == 0 || number == keyboard)
            {
                continue;
            }

            var monitor = monitorOf(number);
            if (monitor == taken)
            {
                continue;
            }

            if (!most.TryGetValue(monitor, out var front))
            {
                most[monitor] = number;
                fronts.Add(number);
            }
            else if (count > At(onGlass, front))
            {
                most[monitor] = number;
                fronts[fronts.IndexOf(front)] = number;
            }
        }

        return fronts;
    }

    private static int At(IReadOnlyList<int> counts, int number) => number < counts.Count ? counts[number] : 0;
}
