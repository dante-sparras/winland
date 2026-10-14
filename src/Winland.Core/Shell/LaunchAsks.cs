namespace Winland.Core.Shell;

/// <summary>
/// The Workspaces programs were started from (ADR 0038). A program takes a moment to open its window, and the keyboard may be on
/// another Workspace by then. Each start is kept until a new window takes it, oldest first, or until it is <see cref="WaitMs"/> old:
/// Windows does not say which start a window came from, so the next new window is taken to be the one that was asked for.
/// </summary>
public sealed class LaunchAsks
{
    /// <summary>How long a start waits for its window, in milliseconds. A program that only brought its open window forward leaves its start to run out.</summary>
    public const long WaitMs = 10_000;

    private readonly Queue<(int Workspace, long Until)> _asks = new();

    /// <summary>Notes that a program was started while <paramref name="workspace"/> had the keyboard.</summary>
    /// <param name="workspace">The Workspace with the keyboard at the start.</param>
    /// <param name="now">Milliseconds that only go up.</param>
    public void Note(int workspace, long now) => _asks.Enqueue((workspace, now + WaitMs));

    /// <summary>The Workspace the next new window is taken to have been started from. Nothing is spent.</summary>
    /// <param name="now">Milliseconds that only go up.</param>
    /// <returns>The Workspace of the oldest start that is not too old, or zero when none waits.</returns>
    public int Peek(long now)
    {
        foreach (var ask in _asks)
        {
            if (now <= ask.Until)
            {
                return ask.Workspace;
            }
        }

        return 0;
    }

    /// <summary>The Workspace a window that just opened was started from. The start is spent.</summary>
    /// <param name="now">Milliseconds that only go up.</param>
    /// <returns>The Workspace of the oldest start that is not too old, or zero when none waits.</returns>
    public int Take(long now)
    {
        while (_asks.TryDequeue(out var ask))
        {
            if (now <= ask.Until)
            {
                return ask.Workspace;
            }
        }

        return 0;
    }
}
