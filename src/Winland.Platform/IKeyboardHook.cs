
namespace Winland.Platform;

/// <summary>
/// The keyboard hook as the desktop uses it. <see cref="KeyboardHook"/> runs it in this process.
/// <see cref="RemoteKeyboardHook"/> runs it in a small process of its own and talks to it over a pipe.
/// Events are raised off the UI thread; post the work elsewhere.
/// </summary>
public interface IKeyboardHook : IDisposable
{
    /// <summary>A chord is waiting in <see cref="TryTake"/>.</summary>
    event Action? ChordQueued;

    /// <summary>The last Super finger went up.</summary>
    event Action? SuperReleased;

    /// <summary>Escape went down while <see cref="EscapeCancels"/> was set.</summary>
    event Action? EscapeTaken;

    /// <summary>
    /// <see cref="TakeHover"/> turned true after <see cref="SamplePointer"/> returned, because the answer came from another process.
    /// Raised on another thread. A hook that answers at once never raises it.
    /// </summary>
    event Action? HoverReady;

    /// <summary>While true, a physical Escape is kept from the focused window and raised as <see cref="EscapeTaken"/>.</summary>
    bool EscapeCancels { get; set; }

    /// <summary>
    /// A key the shell claimed went down while <see cref="PickerTakesKeys"/> was set, and was kept from the chords and from the program in front.
    /// The argument is the shell's key code. Raised off the UI thread; post the work elsewhere.
    /// </summary>
    event Action<int>? PickerKeyTaken;

    /// <summary>
    /// While true, <c>Super+Arrow</c>, <c>Return</c>, <c>Ctrl+A</c>, and <c>R</c> are kept from the focused window and from the chords and raised as
    /// <see cref="PickerKeyTaken"/>. The desktop sets it while the picker is open. A key typed into an administrator window is not seen here.
    /// </summary>
    bool PickerTakesKeys { get; set; }

    /// <summary>
    /// Asked inside the hook when a plain F11 goes down. True keeps that F11 and its key-up from Windows; the answer's owner sends F11 itself.
    /// It must only read and queue, and return at once.
    /// </summary>
    Func<bool>? FullscreenKey { get; set; }

    /// <summary>The binds the ledger honors. Call before <see cref="Install"/>. A hook in another process reads the chord file itself.</summary>
    /// <param name="chords">The binds. A page action is already gated.</param>
    void UseChords(IBindTable chords);

    /// <summary>
    /// While set, bare Escape is taken when <paramref name="binds"/> resolves it, and <see cref="EscapeCancels"/> is not read.
    /// Call before <see cref="Install"/>. A hook in another process ignores this: that process has its own book, and the flag crosses the pipe.
    /// </summary>
    /// <param name="binds">The bind book, including the conditional Escape bind.</param>
    void UseBinds(IBindTable binds);

    /// <summary>
    /// Where a taken Escape goes. When set, the hook calls it instead of raising <see cref="EscapeTaken"/>.
    /// The sink may be called on the hook thread. A hook in another process ignores this and sends Escape across the pipe.
    /// </summary>
    /// <param name="sink">The desktop's sink.</param>
    void UseSink(IGlobalBindSink sink);

    /// <summary>Starts the hook.</summary>
    void Install();

    /// <summary>The next chord the hook accepted, for the UI thread to run.</summary>
    /// <param name="chord">The chord.</param>
    /// <param name="down">True for key-down.</param>
    /// <param name="queuedAt">The <see cref="System.Diagnostics.Stopwatch"/> timestamp when the chord was queued in this process.</param>
    /// <returns>False when none is waiting.</returns>
    bool TryTake(out Chord chord, out bool down, out long queuedAt);

    /// <summary>Queues a chord another hook took, such as the elevated helper's while an administrator window is in front.</summary>
    /// <param name="chord">The chord.</param>
    /// <param name="down">True for key-down.</param>
    void Forward(Chord chord, bool down);

    /// <summary>Super went up where this hook was not called, in an administrator window. The ledger notes it and applies the note on the next key.</summary>
    void ForgetSuper();

    /// <summary>
    /// The shell moved the keyboard, or showed the window that has it, as a Workspace switch does. The hook looks at the window
    /// that has the keyboard again now, where its own turn would be up to a quarter second late.
    /// </summary>
    void KeyboardMoved();

    /// <summary>Logs when the hook thread stops turning. Call from the UI loop.</summary>
    void WatchStall();

    /// <summary>Lets keys held off a hidden window go out once the keyboard is on a window the person can see.</summary>
    void DeliverHeld();

    /// <summary>Reads the pointer, for <see cref="TakeHover"/>. A hook in another process reads it there and raises <see cref="HoverReady"/>.</summary>
    void SamplePointer();

    /// <summary>True once after the pointer moves while no key other than Super is held.</summary>
    /// <returns>True when hover may focus the window under the pointer.</returns>
    bool TakeHover();
}
