namespace Winland.Core.Shell;

/// <summary>
/// The window that has the keyboard, and whether the window that had it before was already off the glass when the keyboard moved.
/// When the window that has the keyboard closes, hides, or is minimized, Windows hands the keyboard to the next window in its own order,
/// and that can be one Winoma keeps hidden. A window that takes the keyboard while the one before is still on the glass asked for it.
/// The shell asks which it was when a hidden window has the keyboard (<see cref="ShellController.FollowShown"/>).
/// Safe from any thread: the Host notes each move on the thread that watches window events, and the desktop's thread reads.
/// </summary>
public sealed class KeyboardTrail
{
    private readonly Func<long, bool> _onGlass;
    private readonly object _gate = new();
    private long _now;
    private long _left;

    /// <summary>A trail that asks <paramref name="onGlass"/> about the window the keyboard leaves.</summary>
    /// <param name="onGlass">True when a window is drawn: open, visible, not minimized, and under no cloak.</param>
    public KeyboardTrail(Func<long, bool> onGlass)
    {
        ArgumentNullException.ThrowIfNull(onGlass);
        _onGlass = onGlass;
    }

    /// <summary>
    /// The keyboard moved to <paramref name="window"/>. The window that had it is looked at now, while the move is fresh:
    /// one that closes a moment after it handed the keyboard on was still on the glass when it did.
    /// </summary>
    /// <param name="window">The window the Host reports in front. Zero, and the window that already has the keyboard, are no move.</param>
    public void Note(long window)
    {
        lock (_gate)
        {
            if (window == 0 || window == _now)
            {
                return;
            }

            var before = _now;
            _now = window;
            _left = before != 0 && !_onGlass(before) ? before : 0;
        }
    }

    /// <summary>The window the keyboard left for <paramref name="current"/>, when that window was already off the glass as the keyboard moved.</summary>
    /// <param name="current">The window that has the keyboard now.</param>
    /// <returns>Null when the window before was still on the glass, when nothing had the keyboard before, and when the move was not seen.</returns>
    public long? Left(long current)
    {
        lock (_gate)
        {
            return current != 0 && current == _now && _left != 0 ? _left : null;
        }
    }
}
