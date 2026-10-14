namespace Winland.Core.Shell;

/// <summary>
/// How long a refused window waits before focus is tried again.
/// The first refusal waits one second. Each further refusal of that same window doubles the wait, and the wait stops growing at sixteen seconds.
/// A success, a different window, or <see cref="Reset"/> clears the count and the wait.
/// </summary>
public sealed class FocusRefusal
{
    private const long FirstDelay = 1000;
    private const int DelayCapShift = 4;

    private long _hwnd;
    private int _failures;
    private long _until;

    /// <summary>The window that is backing off, or zero when no refusal is held.</summary>
    public long Window => _hwnd;

    /// <summary>
    /// True when <paramref name="hwnd"/> must not be focused at <paramref name="now"/>.
    /// The caller then skips the focus plan and does not clear the foreground lock.
    /// </summary>
    /// <param name="hwnd">Window identity.</param>
    /// <param name="now">Clock time in milliseconds. The caller supplies it.</param>
    /// <returns>True while that window's wait has not elapsed.</returns>
    public bool Blocks(long hwnd, long now) => hwnd != 0 && hwnd == _hwnd && now < _until;

    /// <summary>
    /// Records a refusal of <paramref name="hwnd"/> at <paramref name="now"/>.
    /// The wait is one second the first time, then two, four, eight, and sixteen seconds, and it stays at sixteen.
    /// A different window drops the previous count, and this refusal starts at one second.
    /// </summary>
    /// <param name="hwnd">Window identity. Zero is ignored.</param>
    /// <param name="now">Clock time in milliseconds. The wait is measured from this instant.</param>
    public void Refuse(long hwnd, long now)
    {
        if (hwnd == 0)
        {
            return;
        }

        if (hwnd != _hwnd)
        {
            Clear();
            _hwnd = hwnd;
        }

        _failures++;
        var shift = _failures - 1;
        if (shift > DelayCapShift)
        {
            shift = DelayCapShift;
        }

        _until = now + (FirstDelay << shift);
    }

    /// <summary>The window took focus. The count and the wait are cleared.</summary>
    /// <param name="hwnd">The window that took focus. Zero is ignored. Any other window clears the refusal.</param>
    public void Succeeded(long hwnd)
    {
        if (hwnd != 0)
        {
            Clear();
        }
    }

    /// <summary>A focus is aimed at <paramref name="hwnd"/>. A different window clears the count and the wait.</summary>
    /// <param name="hwnd">Window identity. Zero and the window already backing off leave the wait in place.</param>
    /// <returns>True when a refusal was cleared.</returns>
    public bool ClearFor(long hwnd)
    {
        if (hwnd == 0 || _hwnd == 0 || hwnd == _hwnd)
        {
            return false;
        }

        Clear();
        return true;
    }

    /// <summary>
    /// Clears the count and the wait.
    /// A workspace change calls this, and so does a report that the user moved focus.
    /// </summary>
    public void Reset() => Clear();

    private void Clear()
    {
        _hwnd = 0;
        _failures = 0;
        _until = 0;
    }
}
