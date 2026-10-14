namespace Winland.Core.Input;

/// <summary>Why the keyboard hook should be installed again.</summary>
public enum HookRepairReason
{
    /// <summary>Keys reached Windows and the hook was not called for them. Windows removes a hook that missed its timeout and does not say so.</summary>
    Gone,

    /// <summary>The hook was called long after the key happened. Windows may already have skipped it, and may remove it next.</summary>
    Late,

    /// <summary>A Super went past the hook to Windows. A hook installed later sits ahead of this one.</summary>
    Behind,

    /// <summary>The session was unlocked. The secure desktop can stall the hook thread past its timeout.</summary>
    Unlocked,
}

/// <summary>
/// Decides when the keyboard hook is installed again. The hook is installed once and stays put.
/// It is installed again only on evidence that it is gone or behind another hook, never on a timer to sit first in the chain.
/// One thread owns an instance: the hook thread, inside and outside the hook callback.
/// </summary>
public sealed class HookRepair
{
    /// <summary>Keys Windows reported in a row without one hook call before the hook counts as gone.</summary>
    public const int MissedKeysLimit = 3;

    /// <summary>How late a hook call may start before the hook is installed again, in milliseconds.</summary>
    public const long LateLimitMs = 200;

    /// <summary>The shortest gap between two installs, in milliseconds.</summary>
    public const long MinGapMs = 1000;

    /// <summary>The longest gap a repeated fight with another hook backs off to, in milliseconds.</summary>
    public const long MaxGapMs = 30_000;

    /// <summary>An install this long past the end of the previous gap ends a backoff, in milliseconds.</summary>
    public const long CalmMs = 10_000;

    private int _missed;
    private HookRepairReason? _pending;
    private long _installedAt = long.MinValue;
    private long _gap = MinGapMs;

    /// <summary>The hook was called for a physical key. Clears the count of missed keys.</summary>
    public void HookCalled() => _missed = 0;

    /// <summary>
    /// The hook was called for a physical key that happened <paramref name="lagMs"/> earlier.
    /// A call later than <see cref="LateLimitMs"/> asks for an install.
    /// </summary>
    /// <param name="lagMs">Milliseconds between the key event and the start of the hook call.</param>
    /// <returns>True when this call was late.</returns>
    public bool HookCalled(long lagMs)
    {
        _missed = 0;
        if (lagMs < LateLimitMs)
        {
            return false;
        }

        Ask(HookRepairReason.Late);
        return true;
    }

    /// <summary>
    /// Raw input reports a physical key that reached Windows.
    /// A hook that is in the chain is called for every such key, so <see cref="MissedKeysLimit"/> of them in a row means it is gone.
    /// </summary>
    /// <param name="hookCannotSee">
    /// Asked only when the count is reached. True while the hook is not called for a legitimate reason,
    /// such as an administrator window in the foreground. The count then starts over.
    /// </param>
    /// <returns>True when this key marked the hook as gone.</returns>
    public bool KeyReachedWindows(Func<bool> hookCannotSee)
    {
        ArgumentNullException.ThrowIfNull(hookCannotSee);
        _missed++;
        if (_missed < MissedKeysLimit)
        {
            return false;
        }

        _missed = 0;
        if (hookCannotSee())
        {
            return false;
        }

        // A hook that is gone has no other hook to fight, so it does not wait out the backoff a fight built up.
        // One that is already waiting keeps its gap, which a refused install has doubled.
        if (_pending != HookRepairReason.Gone)
        {
            _gap = MinGapMs;
        }

        Ask(HookRepairReason.Gone);
        return true;
    }

    /// <summary>A Super reached Windows without this hook swallowing it.</summary>
    public void SuperWentPast() => Ask(HookRepairReason.Behind);

    /// <summary>The Windows session was unlocked.</summary>
    public void Unlocked() => Ask(HookRepairReason.Unlocked);

    /// <summary>True when a reason is waiting, whether or not its gap has passed.</summary>
    public bool Pending => _pending is not null;

    /// <summary>
    /// The reason to install now, or null. A reason that arrives inside the gap waits for it.
    /// Installs close together for any reason but <see cref="HookRepairReason.Gone"/> double the gap, up to <see cref="MaxGapMs"/>,
    /// so a fight with another hook that also installs again does not spin.
    /// </summary>
    /// <param name="now">Tick count in milliseconds.</param>
    /// <returns>The reason to install now. Call <see cref="Installed"/> once the install succeeds.</returns>
    public HookRepairReason? Due(long now)
    {
        if (_pending is not HookRepairReason reason)
        {
            return null;
        }

        if (_installedAt != long.MinValue && now - _installedAt < _gap)
        {
            return null;
        }

        return reason;
    }

    /// <summary>The hook was installed again for <paramref name="reason"/>.</summary>
    /// <param name="reason">The reason <see cref="Due"/> returned.</param>
    /// <param name="now">Tick count in milliseconds.</param>
    public void Installed(HookRepairReason reason, long now)
    {
        var close = _installedAt != long.MinValue && now - _installedAt < _gap + CalmMs;
        _gap = reason == HookRepairReason.Gone || !close ? MinGapMs : Math.Min(_gap * 2, MaxGapMs);
        _installedAt = now;
        _pending = null;
        _missed = 0;
    }

    /// <summary>
    /// Installing again failed. The reason stays waiting, and the gap doubles before the next try,
    /// so a refused install is retried without spinning.
    /// </summary>
    /// <param name="now">Tick count in milliseconds.</param>
    public void Failed(long now)
    {
        _gap = _installedAt == long.MinValue ? MinGapMs : Math.Min(_gap * 2, MaxGapMs);
        _installedAt = now;
    }

    private void Ask(HookRepairReason reason)
    {
        // A hook that is gone outranks every other reason.
        if (_pending != HookRepairReason.Gone)
        {
            _pending = reason;
        }
    }
}
