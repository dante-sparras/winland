namespace Winland.Core.Shell;

/// <summary>What the adapter read before asking for a focus. The arbiter does not read the host.</summary>
/// <param name="Target">Window to focus. Zero is not a window.</param>
/// <param name="Foreground">Window that has the keyboard, or zero when there is none.</param>
/// <param name="Elevated">True when <paramref name="Target"/> belongs to an elevated process.</param>
/// <param name="HelperRunning">True when the elevated helper is already running.</param>
/// <param name="SuperHeld">True while the chord ledger records a Super finger.</param>
/// <param name="Now">Clock time in milliseconds. The caller supplies it.</param>
public readonly record struct FocusFacts(long Target, long Foreground, bool Elevated, bool HelperRunning, bool SuperHeld, long Now);

/// <summary>What the adapter should do for one focus.</summary>
public abstract record FocusVerdict
{
    private FocusVerdict()
    {
    }

    /// <summary>The target is already in front. The refusal and the handoff were cleared. Return success.</summary>
    public sealed record AlreadyInFront : FocusVerdict;

    /// <summary>Do nothing and return failure. A refusal is still in force.</summary>
    public sealed record BackOff : FocusVerdict;

    /// <summary>An elevated handoff is still open. Return success and do not ask the broker again.</summary>
    public sealed record HoldHandoff : FocusVerdict;

    /// <summary>Ask the elevated broker to focus the target. The handoff lasts two seconds.</summary>
    public sealed record HandOff : FocusVerdict;

    /// <summary>Run <see cref="FocusPlan.For"/> for <paramref name="Kind"/>.</summary>
    /// <param name="Kind">Which focus to run.</param>
    /// <param name="SuperHeld">The Super fact to pass. A helper attempt passes false, so the plan never joins.</param>
    public sealed record Attempt(FocusKind Kind, bool SuperHeld) : FocusVerdict;
}

/// <summary>
/// Decides whether a focus backs off, holds a handoff, asks the elevated broker, or runs a plan.
/// The adapter reads the facts and performs the verdict. An iconic window is restored before the facts are read.
/// Not thread-safe.
/// </summary>
public sealed class FocusArbiter
{
    private const long HandoffMilliseconds = 2000;

    private readonly FocusRefusal _refusal = new();
    private long _refusedForeground;
    private long _handoff;
    private long _handoffUntil;

    /// <summary>
    /// Decides what the adapter should do with <paramref name="facts"/>.
    /// A target of zero backs off.
    /// A target that is already in front clears the refusal and the handoff.
    /// Asking again for the refused window after the foreground has moved clears that refusal and leaves an open handoff in place.
    /// An open handoff for this target lasts until two seconds after it was armed, or until another window took focus (<see cref="Succeeded"/>). The broker is not asked again while it lasts.
    /// A refusal still in force backs off, and no plan is selected.
    /// A different window drops the refusal.
    /// An elevated target is handed to the broker when the helper is running. Otherwise the attempt is <see cref="FocusKind.Helper"/> with Super not held.
    /// Any other target attempts <see cref="FocusKind.Workspace"/> with the Super fact.
    /// </summary>
    /// <param name="facts">The adapter's readings.</param>
    /// <returns>The verdict to perform.</returns>
    public FocusVerdict Decide(FocusFacts facts)
    {
        if (facts.Target == 0)
        {
            return new FocusVerdict.BackOff();
        }

        if (facts.Foreground == facts.Target)
        {
            _refusal.Succeeded(facts.Target);
            _refusedForeground = 0;
            _handoff = 0;
            _handoffUntil = 0;
            return new FocusVerdict.AlreadyInFront();
        }

        if (_refusal.Window == facts.Target
            && _refusedForeground != 0
            && facts.Foreground != 0
            && facts.Foreground != _refusedForeground)
        {
            _refusal.Reset();
            _refusedForeground = 0;
        }

        if (facts.Target == _handoff && facts.Now < _handoffUntil)
        {
            return new FocusVerdict.HoldHandoff();
        }

        if (_refusal.Blocks(facts.Target, facts.Now))
        {
            return new FocusVerdict.BackOff();
        }

        if (_refusal.ClearFor(facts.Target))
        {
            _refusedForeground = 0;
        }

        if (facts.Elevated)
        {
            if (facts.HelperRunning)
            {
                _handoff = facts.Target;
                _handoffUntil = facts.Now + HandoffMilliseconds;
                return new FocusVerdict.HandOff();
            }

            return new FocusVerdict.Attempt(FocusKind.Helper, false);
        }

        return new FocusVerdict.Attempt(FocusKind.Workspace, facts.SuperHeld);
    }

    /// <summary>
    /// The attempt put a window in front. The refusal is cleared.
    /// A handoff for another window ends, since that window no longer has the keyboard, and the broker is asked again for it.
    /// </summary>
    /// <param name="target">The window that took focus. Zero is ignored.</param>
    public void Succeeded(long target)
    {
        _refusal.Succeeded(target);
        if (target == 0)
        {
            return;
        }

        _refusedForeground = 0;
        if (target != _handoff)
        {
            _handoff = 0;
            _handoffUntil = 0;
        }
    }

    /// <summary>
    /// The attempt left another window in front.
    /// The wait starts at one second, then doubles for that same window, and the foreground is remembered so a later move can clear the refusal.
    /// </summary>
    /// <param name="target">The window that stayed back. Zero is ignored.</param>
    /// <param name="foreground">The foreground from the attempt's facts.</param>
    /// <param name="now">Clock time in milliseconds. The wait is measured from this instant.</param>
    public void Failed(long target, long foreground, long now)
    {
        if (target == 0)
        {
            return;
        }

        _refusedForeground = foreground;
        _refusal.Refuse(target, now);
    }

    /// <summary>Clears the refusal, the remembered foreground, and the handoff. A workspace change calls this.</summary>
    public void Reset()
    {
        _refusal.Reset();
        _refusedForeground = 0;
        _handoff = 0;
        _handoffUntil = 0;
    }
}
