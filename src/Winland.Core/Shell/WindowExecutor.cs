namespace Winland.Core;

/// <summary>
/// Turns a direct place, raise, close, or mark into an elevated act, and says when a window in front needs the helper.
/// The adapter performs the Win32 call and records the act. This type does not call Win32, and it does not coalesce.
/// </summary>
public static class WindowExecutor
{
    /// <summary>Win32 <c>ERROR_ACCESS_DENIED</c>. A failed direct place or raise with this error needs the helper.</summary>
    private const int AccessDenied = 5;

    /// <summary>
    /// The elevated place when a direct place failed with access denied.
    /// Success produces none. Any other error produces none. The rectangle is the one the direct call used.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="succeeded">True when the direct place returned success.</param>
    /// <param name="error">The Win32 error when <paramref name="succeeded"/> is false. Ignored when the place succeeded.</param>
    /// <returns>The place, or null when the helper is not needed.</returns>
    public static ElevatedAct? Place(long id, int x, int y, int width, int height, bool succeeded, int error)
    {
        if (succeeded || error != AccessDenied)
        {
            return null;
        }

        return ElevatedAct.Place(id, x, y, width, height);
    }

    /// <summary>
    /// The elevated raise when a direct raise failed with access denied.
    /// Success produces none. Any other error produces none.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="succeeded">True when the direct raise returned success.</param>
    /// <param name="error">The Win32 error when <paramref name="succeeded"/> is false. Ignored when the raise succeeded.</param>
    /// <returns>The raise, or null when the helper is not needed.</returns>
    public static ElevatedAct? Raise(long id, bool succeeded, int error)
    {
        if (succeeded || error != AccessDenied)
        {
            return null;
        }

        return ElevatedAct.Raise(id);
    }

    /// <summary>
    /// The elevated mark when the desktop's own write of a window's Workspace failed with access denied (ADR 0078).
    /// An administrator window refuses a property from a desktop that is not one, and it is hidden with its Workspace like any window,
    /// so without its mark a desktop that starts would not know where it belongs. Success produces none. Any other error produces none.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="mark">The mark the direct call tried to write. <see cref="WorkspaceMark.None"/> when it tried to take the mark off.</param>
    /// <param name="succeeded">True when the direct call returned success.</param>
    /// <param name="error">The Win32 error when <paramref name="succeeded"/> is false. Ignored when the call succeeded.</param>
    /// <returns>The mark, or null when the helper is not needed.</returns>
    public static ElevatedAct? Mark(long id, int mark, bool succeeded, int error)
    {
        if (succeeded || error != AccessDenied)
        {
            return null;
        }

        return ElevatedAct.Mark(id, mark);
    }

    /// <summary>
    /// The elevated close when the window is elevated.
    /// A normal window produces none, and the adapter closes it directly.
    /// </summary>
    /// <param name="id">Window identity.</param>
    /// <param name="elevated">True when the window's process is elevated. The adapter reads that fact.</param>
    /// <returns>The close, or null when the adapter should close directly.</returns>
    public static ElevatedAct? Close(long id, bool elevated) =>
        elevated ? ElevatedAct.Close(id) : null;

    /// <summary>
    /// True when the window that took the foreground needs the helper started.
    /// The desktop hook is not called for an administrator window's keys, so the helper's hook takes the chords over it.
    /// Windows can bring such a window forward itself, as Ctrl+Shift+Esc does Task Manager, and nothing is placed, raised, or closed then.
    /// </summary>
    /// <param name="elevated">True when the window's process is elevated. The adapter reads that fact.</param>
    /// <param name="helperRunning">True when the helper is already running.</param>
    /// <param name="refused">
    /// True when the helper could not be started, consent for it was refused, or it left without connecting
    /// (<see cref="LeftUnconnected"/>). It is not asked for again.
    /// </param>
    /// <returns>True when the adapter should start the helper.</returns>
    public static bool Front(bool elevated, bool helperRunning, bool refused) =>
        elevated && !helperRunning && !refused;

    /// <summary>
    /// True when a helper that exited counts as refused: its pipe never connected.
    /// Consent given with another administrator account starts a helper the pipe turns away, and a helper can fail as it starts.
    /// Each would be asked for again at every administrator window, and the question is asked once (ADR 0065).
    /// A helper that connected and exited later is started again.
    /// </summary>
    /// <param name="exited">True when the helper the desktop started last has exited.</param>
    /// <param name="connected">True when that helper's pipe connected at any time.</param>
    /// <returns>True when the helper is not asked for again until the desktop restarts.</returns>
    public static bool LeftUnconnected(bool exited, bool connected) =>
        exited && !connected;
}
