namespace Winland.Domain;

/// <summary>
/// Which Client has focus, the Client each Workspace gives focus back to when it is shown again, and the order in
/// which Clients were focused. The Focused client changes only through <see cref="Take"/>, and a Workspace's
/// remembered Client only through <see cref="Take"/> and <see cref="Remember"/>.
/// </summary>
/// <param name="canTake">False for a window that takes no focus, such as a no_focus or modal-blocked one.</param>
internal sealed class FocusState(Func<WindowId, bool> canTake)
{
    private readonly List<WindowId> _history = [];

    /// <summary>The Focused client, or null when no Client has focus.</summary>
    public WindowId? Focused { get; private set; }

    /// <summary>The Clients that had focus, the newest first.</summary>
    public IReadOnlyList<WindowId> History => _history;

    /// <summary>A window Winland does not manage that has the foreground, while it may still become a Client.</summary>
    public WindowId? Unmanaged { get; private set; }

    /// <summary>The Focused client when <see cref="Unmanaged"/> took the foreground.</summary>
    public WindowId? BeforeUnmanaged { get; private set; }

    /// <summary>Focus moves to <paramref name="window"/>, and <paramref name="desk"/>, when given, remembers it.</summary>
    public void Take(WindowId? window, LiveWorkspace? desk = null)
    {
        Focused = window;
        desk?.RememberFocus(window);
    }

    /// <summary><paramref name="desk"/> gives focus back to <paramref name="window"/> when it is next shown.</summary>
    public static void Remember(LiveWorkspace desk, WindowId? window)
    {
        ArgumentNullException.ThrowIfNull(desk);
        desk.RememberFocus(window);
    }

    /// <summary><paramref name="window"/> became the newest in the history.</summary>
    public void Confirm(WindowId window)
    {
        _history.Remove(window);
        _history.Insert(0, window);
    }

    /// <summary>A window Winland does not manage took the foreground from the Focused client.</summary>
    public void UnmanagedTook(WindowId window)
    {
        BeforeUnmanaged = Focused;
        Unmanaged = window;
    }

    /// <summary>
    /// <paramref name="window"/> became a Client. When it was the unmanaged window that took the foreground, the
    /// Client focused before it is returned, so it can have the focus back.
    /// </summary>
    public WindowId? Adopted(WindowId window)
    {
        if (Unmanaged != window)
            return null;

        var prior = BeforeUnmanaged;
        Unmanaged = null;
        BeforeUnmanaged = null;
        return prior;
    }

    /// <summary>A closed window leaves the history and the unmanaged marks. The Focused client is moved by the close itself.</summary>
    public void Forget(WindowId window)
    {
        _history.Remove(window);
        if (Unmanaged == window)
        {
            Unmanaged = null;
            BeforeUnmanaged = null;
        }

        if (BeforeUnmanaged == window)
            BeforeUnmanaged = null;
    }

    /// <summary>
    /// How recently <paramref name="window"/> had focus: the newest is highest. One never focused is 0, below every
    /// focused one, as a window that opens goes at the old end of the history.
    /// </summary>
    public int Recency(WindowId window)
    {
        var index = _history.IndexOf(window);
        return index < 0 ? 0 : _history.Count - index;
    }

    /// <summary>The Client <paramref name="desk"/> gives focus back to, unless that Client takes no focus.</summary>
    public WindowId? Remembered(LiveWorkspace desk)
    {
        ArgumentNullException.ThrowIfNull(desk);
        return desk.Remembered() is WindowId window && canTake(window) ? window : null;
    }

    /// <summary>The newest Client in the history on <paramref name="desk"/> that is not minimized and can take focus.</summary>
    public WindowId? Recent(LiveWorkspace desk)
    {
        ArgumentNullException.ThrowIfNull(desk);
        foreach (var window in _history)
        {
            if (desk.ClientOf(window) is { Minimized: false } && !desk.IsWaiting(window) && canTake(window))
                return window;
        }

        return null;
    }

    /// <summary>
    /// The Client that takes focus on <paramref name="source"/> after the focused one left it: the one window that
    /// leaving revealed, or else the last visible Client.
    /// </summary>
    public static WindowId? AfterLeaving(LiveWorkspace source, IReadOnlyList<WindowId> revealed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(revealed);
        return revealed.Count == 1 && source.ClientOf(revealed[0]) is not null ? revealed[0] : source.LastVisible();
    }
}
