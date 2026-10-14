using Winland.Core.Input;

namespace Winland.Platform;

/// <summary>
/// The shell state the keyboard hook reads. The shell pushes a whole snapshot and <see cref="Apply"/> replaces this.
/// The hook never waits on that push: it reads what is already here. Until the first push, the picker is closed,
/// Escape is not claimed, and the capture picker's keys are loaded so a hook process that only opens the picker still matches them.
/// </summary>
public static class ShellState
{
    private static int _escape;
    private static int _session;
    private static int _surface;

    static ShellState() => Picker.Use(open: false, PickerGate.CaptureKeys);

    /// <summary>The picker's open flag and the keys it wants. The hook calls <see cref="PickerGate.OnKey"/> on its own thread.</summary>
    public static PickerGate Picker { get; } = new();

    /// <summary>True while a conditional bind the shell asked for is live. Dictation's Escape is that bind.</summary>
    public static bool Escape => Volatile.Read(ref _escape) != 0;

    /// <summary>True while the Windows session is locked, or a lock has been asked for.</summary>
    public static bool SessionLocked => Volatile.Read(ref _session) != 0;

    /// <summary>True while one of the shell's windows has the keyboard, such as an open panel.</summary>
    public static bool SurfaceHeld => Volatile.Read(ref _surface) != 0;

    /// <summary>Replaces the cache with one push. Safe to call from the pipe thread while the hook reads.</summary>
    /// <param name="pickerOpen">True while the capture picker is open.</param>
    /// <param name="escape">True while the shell wants the Escape bind live.</param>
    /// <param name="sessionLocked">True while the session is locked or a lock has been asked for.</param>
    /// <param name="surfaceHeld">True while a shell window has the keyboard.</param>
    /// <param name="keys">The keys the picker wants reported.</param>
    public static void Apply(bool pickerOpen, bool escape, bool sessionLocked, bool surfaceHeld, IReadOnlyList<KeptKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        Picker.Use(pickerOpen, keys);
        Volatile.Write(ref _escape, escape ? 1 : 0);
        Volatile.Write(ref _session, sessionLocked ? 1 : 0);
        Volatile.Write(ref _surface, surfaceHeld ? 1 : 0);
    }
}
