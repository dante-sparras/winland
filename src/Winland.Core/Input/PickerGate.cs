namespace Winland.Core.Input;

/// <summary>
/// One key the shell wants reported while its capture picker is open. The hook matches this. It does not ask the shell.
/// </summary>
/// <param name="Vk">The virtual-key code.</param>
/// <param name="Super">True when Super must be held.</param>
/// <param name="Ctrl">True when Control must be held.</param>
/// <param name="Alt">True when Alt must be held.</param>
/// <param name="Id">The number the shell understands. The hook reports it and does not know the name.</param>
public readonly record struct KeptKey(int Vk, bool Super, bool Ctrl, bool Alt, int Id);

/// <summary>What the hook does with one key event while the picker may be open.</summary>
/// <param name="Kept">True when the event is kept from the chords and from the program in front.</param>
/// <param name="Id">The shell's id for this press, or null when the event presses nothing the shell named.</param>
public readonly record struct PickerTake(bool Kept, int? Id)
{
    /// <summary>The event goes on to the chords and the program in front.</summary>
    public static PickerTake Pass => default;
}

/// <summary>
/// Decides which key events the hook keeps while the shell's picker is open.
/// The shell pushes <see cref="Use"/>: whether the picker is open, and which keys it wants reported.
/// No key-down reaches the program in front while it is open, except Print Screen, which still dismisses it.
/// Super, Control, and Alt are remembered from their own key events, because swallowing Super means the hook's ledger
/// no longer reports it. A key that was already down still releases, so the program is not left holding half a key.
/// <see cref="OnKey"/> runs on the hook thread. <see cref="Use"/> and <see cref="Open"/> may run on another.
/// </summary>
public sealed class PickerGate
{
    private const int VkSnapshot = 0x2C;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkControl = 0x11;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkMenu = 0x12;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;

    /// <summary>
    /// The capture picker's keys, numbered the way <c>PickerKey</c> numbers them.
    /// Left is 1, Right 2, Up 3, Down 4, Return 5, Monitor 6, Restore 7, Scroll 8. Escape is a separate claim.
    /// </summary>
    public static IReadOnlyList<KeptKey> CaptureKeys { get; } =
    [
        new(0x25, Super: true, Ctrl: false, Alt: false, Id: 1),
        new(0x27, Super: true, Ctrl: false, Alt: false, Id: 2),
        new(0x26, Super: true, Ctrl: false, Alt: false, Id: 3),
        new(0x28, Super: true, Ctrl: false, Alt: false, Id: 4),
        new(0x0D, Super: false, Ctrl: false, Alt: false, Id: 5),
        new(0x41, Super: false, Ctrl: true, Alt: false, Id: 6),
        new(0x52, Super: false, Ctrl: false, Alt: false, Id: 7),
        new(0x53, Super: false, Ctrl: false, Alt: false, Id: 8),
    ];

    private readonly HashSet<int> _kept = [];
    private readonly HashSet<int> _super = [];
    private readonly HashSet<int> _ctrl = [];
    private readonly HashSet<int> _alt = [];
    private KeptKey[] _keys = [];
    private int _open;

    /// <summary>True while the picker is open. The shell sets it, and the hook reads it.</summary>
    public bool Open
    {
        get => Volatile.Read(ref _open) != 0;
        set => Volatile.Write(ref _open, value ? 1 : 0);
    }

    /// <summary>
    /// Replaces the open flag and the keys the shell wants reported. A key the hook already kept still releases on key-up.
    /// </summary>
    /// <param name="open">True while the picker is open.</param>
    /// <param name="keys">The keys to report. Empty reports nothing, and still keeps every other key-down while open.</param>
    public void Use(bool open, IReadOnlyList<KeptKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        Volatile.Write(ref _keys, keys as KeptKey[] ?? [.. keys]);
        Volatile.Write(ref _open, open ? 1 : 0);
    }

    /// <summary>Decides one physical key event.</summary>
    /// <param name="vk">The virtual-key code.</param>
    /// <param name="down">True for key-down.</param>
    /// <param name="super">True while Super is held, not counting this key. A swallowed Super is remembered here as well.</param>
    /// <param name="ctrl">True while Control is held, not counting this key.</param>
    /// <param name="alt">True while Alt is held, not counting this key.</param>
    /// <returns>What to do with the event.</returns>
    public PickerTake OnKey(int vk, bool down, bool super, bool ctrl, bool alt)
    {
        if (!down)
        {
            Forget(vk);

            // A key the picker took goes up unseen, even when the picker has closed since.
            return _kept.Remove(vk) ? new PickerTake(true, null) : PickerTake.Pass;
        }

        if (!Open || vk == VkSnapshot)
        {
            return PickerTake.Pass;
        }

        Note(vk);
        _kept.Add(vk);
        var held = new Held(super || _super.Count > 0, ctrl || _ctrl.Count > 0, alt || _alt.Count > 0);
        return new PickerTake(true, Match(vk, held));
    }

    private int? Match(int vk, Held held)
    {
        var keys = Volatile.Read(ref _keys) ?? [];
        foreach (var key in keys)
        {
            if (key.Vk == vk && key.Super == held.Super && key.Ctrl == held.Ctrl && key.Alt == held.Alt)
            {
                return key.Id;
            }
        }

        return null;
    }

    private void Note(int vk)
    {
        if (vk is VkLWin or VkRWin)
        {
            _super.Add(vk);
        }
        else if (vk is VkControl or VkLControl or VkRControl)
        {
            _ctrl.Add(vk);
        }
        else if (vk is VkMenu or VkLMenu or VkRMenu)
        {
            _alt.Add(vk);
        }
    }

    private void Forget(int vk)
    {
        _super.Remove(vk);
        _ctrl.Remove(vk);
        _alt.Remove(vk);
    }

    private readonly record struct Held(bool Super, bool Ctrl, bool Alt);
}
