namespace Winland;

/// <summary>
/// What the display fade does after a request or a tick: the black layer's opacity, null when it is hidden, whether to
/// turn the displays off or on now, and how many milliseconds until the next tick, or null when nothing is pending.
/// </summary>
public readonly record struct DpmsFadeStep(double? Alpha, bool PowerOff, bool PowerOn, int? NextTick);

/// <summary>
/// <c>animation:fadeDpms</c>, as Winland does it: turning the displays off fades them to black first and
/// turns them off at the end; turning them on starts black and fades out once they show again. On Windows any input
/// wakes displays that were turned off, so input after the turn-off counts as turning them on, and the black never
/// stays over displays that are on. Times are in milliseconds. Used on one thread.
/// </summary>
public sealed class DpmsFader
{
    /// <summary>The ticks while the layer is changing.</summary>
    public const int FrameMilliseconds = 16;

    /// <summary>The ticks while the displays are off, to notice input that woke them.</summary>
    public const int WatchMilliseconds = 200;

    /// <summary>How long woken displays get to show again before the fade starts, unless Windows says they show.</summary>
    public const int WakeMilliseconds = 1500;

    private Phase _phase;
    private double _alpha;
    private double _from;
    private double _to;
    private long _since;
    private Timing _timing;

    private enum Phase
    {
        Clear,
        Darkening,
        Dark,
        Waking,
        Lightening,
    }

    /// <summary>True while the displays are off and the layer waits for input or a turn-on.</summary>
    public bool Off => _phase == Phase.Dark;

    /// <summary>True while the black layer shows.</summary>
    public bool Showing => _phase != Phase.Clear;

    /// <summary><c>dpms off</c>. With no <paramref name="fade"/> the displays turn off at once.</summary>
    public DpmsFadeStep TurnOff(Timing? fade, long now)
    {
        if (fade is not { Milliseconds: > 0 } timing)
        {
            _phase = Phase.Clear;
            _alpha = 0;
            return new DpmsFadeStep(null, true, false, null);
        }

        if (_phase is Phase.Dark or Phase.Darkening)
            return Hold();

        _timing = timing;
        Begin(Phase.Darkening, 1, now);
        return Tick(now);
    }

    /// <summary><c>dpms on</c>. The displays turn on at once; with a <paramref name="fade"/> the black fades out.</summary>
    public DpmsFadeStep TurnOn(Timing? fade, long now)
    {
        if (fade is not { Milliseconds: > 0 } timing)
        {
            _phase = Phase.Clear;
            _alpha = 0;
            return new DpmsFadeStep(null, false, true, null);
        }

        _timing = timing;
        switch (_phase)
        {
            case Phase.Clear:
                return new DpmsFadeStep(null, false, true, null);
            case Phase.Darkening:
                // Not off yet: the black goes back from where it got to.
                Begin(Phase.Lightening, 0, now);
                return Tick(now) with { PowerOn = true };
            case Phase.Dark:
                _phase = Phase.Waking;
                _since = now;
                return new DpmsFadeStep(_alpha, false, true, WatchMilliseconds);
            default:
                return Hold();
        }
    }

    /// <summary>Input came after the displays were turned off, which wakes them on Windows.</summary>
    public DpmsFadeStep InputSeen(long now)
    {
        if (_phase != Phase.Dark)
            return Hold();

        _phase = Phase.Waking;
        _since = now;
        return Hold();
    }

    /// <summary>Windows says the displays show again.</summary>
    public DpmsFadeStep DisplaysShown(long now)
    {
        if (_phase is not (Phase.Dark or Phase.Waking))
            return Hold();

        Begin(Phase.Lightening, 0, now);
        return Tick(now);
    }

    public DpmsFadeStep Tick(long now)
    {
        switch (_phase)
        {
            case Phase.Darkening or Phase.Lightening:
                var elapsed = now - _since;
                if (elapsed < _timing.Milliseconds)
                {
                    _alpha = _from + ((_to - _from) * _timing.Curve.Ease(elapsed / _timing.Milliseconds));
                    return Hold();
                }

                _alpha = _to;
                if (_phase == Phase.Darkening)
                {
                    _phase = Phase.Dark;
                    return new DpmsFadeStep(_alpha, true, false, WatchMilliseconds);
                }

                _phase = Phase.Clear;
                return Hold();
            case Phase.Waking when now - _since >= WakeMilliseconds:
                Begin(Phase.Lightening, 0, now);
                return Tick(now);
            default:
                return Hold();
        }
    }

    private void Begin(Phase phase, double to, long now)
    {
        _phase = phase;
        _from = _alpha;
        _to = to;
        _since = now;
    }

    private DpmsFadeStep Hold() => _phase switch
    {
        Phase.Clear => new DpmsFadeStep(null, false, false, null),
        Phase.Darkening or Phase.Lightening => new DpmsFadeStep(_alpha, false, false, FrameMilliseconds),
        _ => new DpmsFadeStep(_alpha, false, false, WatchMilliseconds),
    };
}
