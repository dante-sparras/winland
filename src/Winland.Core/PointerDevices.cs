namespace Winland;

/// <summary>
/// One pointer's <c>device[name]</c> overrides. An unset field keeps the global <c>input</c> key. The name is the
/// spelling the <c>devices</c> reply uses.
/// </summary>
public sealed record DevicePointer(string Name, double? Sensitivity, double? ScrollFactor, int? Rotation);

/// <summary>
/// The per-device pointer overrides, with the global input they fall back to. The mouse hook reads one published
/// instance with no lock. Sensitivity, scroll factor and rotation are the only fields Windows can apply to one
/// pointer. The others are Windows limits.
/// </summary>
public sealed class PointerDevices
{
    private readonly Dictionary<string, DevicePointer> _byName;

    private PointerDevices(IReadOnlyList<DevicePointer> pointers, double scrollBaseline, int globalRotation)
    {
        Pointers = pointers;
        _byName = new Dictionary<string, DevicePointer>(pointers.Count, StringComparer.Ordinal);
        foreach (var pointer in pointers)
            _byName[pointer.Name] = pointer;

        ScrollBaseline = scrollBaseline;
        GlobalRotation = globalRotation;
        Active = pointers.Count > 0;
        DefersMotion = false;
        foreach (var pointer in pointers)
        {
            if (pointer.Sensitivity is not null || pointer.Rotation is not null)
            {
                DefersMotion = true;
                break;
            }
        }
    }

    public static PointerDevices Empty { get; } = new([], 1, 0);

    public IReadOnlyList<DevicePointer> Pointers { get; }

    /// <summary>A device sets <c>sensitivity</c>, <c>scroll_factor</c> or <c>rotation</c>, so pointer raw input is read.</summary>
    public bool Active { get; }

    /// <summary>
    /// A device sets <c>sensitivity</c> or <c>rotation</c>, including a rotation of 0. The hook must not turn the move
    /// itself: taking it drops the raw packet that names the mouse, and the turn is applied when that packet arrives.
    /// </summary>
    public bool DefersMotion { get; }

    /// <summary>The global <c>scroll_factor</c>, or 1 when that key is unset. A device factor is divided by this.</summary>
    public double ScrollBaseline { get; }

    /// <summary>The global factor is 0, so the wheel lines are 0 and a device factor cannot bring the wheel back.</summary>
    public bool GlobalScrollStops => ScrollBaseline <= 0;

    /// <summary>The global <c>rotation</c>, used for a device that sets none of its own.</summary>
    public int GlobalRotation { get; }

    public static PointerDevices From(ConfigDocument document, InputOptions input)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(input);
        if (document.DevicePointers.Count == 0)
            return new([], input.ScrollFactor ?? 1, input.Rotation);

        return new(document.DevicePointers, input.ScrollFactor ?? 1, input.Rotation);
    }

    /// <summary>The device's <c>scroll_factor</c> when that field is set.</summary>
    public bool TryScrollFactor(string name, out double factor)
    {
        ArgumentNullException.ThrowIfNull(name);
        factor = 0;
        if (!_byName.TryGetValue(InputDeviceNames.Internal(name), out var pointer) || pointer.ScrollFactor is not double set)
            return false;

        factor = set;
        return true;
    }

    /// <summary>
    /// The pointer speed the device's <c>sensitivity</c> maps to, when that field is set, and the rotation to apply:
    /// the device's, or the global one when the device sets none.
    /// </summary>
    public PointerMotion MotionOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_byName.TryGetValue(InputDeviceNames.Internal(name), out var pointer))
            return new PointerMotion(null, GlobalRotation);

        var speed = pointer.Sensitivity is double sensitivity ? InputMapping.MouseSpeed(sensitivity) : (int?)null;
        return new PointerMotion(speed, pointer.Rotation ?? GlobalRotation);
    }
}

/// <summary>How one device's moves are scaled and turned. A null speed keeps the speed Windows is already using.</summary>
public readonly record struct PointerMotion(int? Speed, int Rotation);

/// <summary>
/// The difference between the move Windows already applied and the move one device's settings ask for. Movement is
/// paired with the raw packet that names the mouse, because the mouse hook does not. A wheel carries its own delta,
/// so it is not queued. Used on the input thread.
/// </summary>
public sealed class PointerCorrection
{
    public const int QueueLimit = 32;
    public const long StaleMs = 50;
    public const int DefaultSpeed = 10;

    private readonly Queue<Noted> _moves = new();
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);

    /// <summary>A move the hook let through, in pixels, to be matched with the raw packet that follows it.</summary>
    public void NoteMove(int dx, int dy, long nowMs)
    {
        if (dx == 0 && dy == 0)
            return;

        if (_moves.Count == QueueLimit)
            _moves.Dequeue();

        _moves.Enqueue(new Noted(dx, dy, nowMs));
    }

    /// <summary>
    /// The extra pixels to send so this device's move lands where its sensitivity and rotation ask. Null names no
    /// device: the oldest move is dropped and nothing is sent. A move older than <see cref="StaleMs"/> clears the
    /// queue, so a late packet is not paired with a later move.
    /// </summary>
    public (int Dx, int Dy)? TakeMove(PointerDevices devices, string? device, int systemSpeed, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (_moves.Count == 0)
            return null;

        if (nowMs - _moves.Peek().At > StaleMs)
        {
            _moves.Clear();
            return null;
        }

        var noted = _moves.Dequeue();
        if (device is null)
            return null;

        var motion = devices.MotionOf(device);
        var system = systemSpeed is >= 1 and <= 20 ? systemSpeed : DefaultSpeed;
        var speed = motion.Speed ?? system;
        var scale = speed / (double)system;
        var state = StateFor(device);
        state.ScaleX += noted.X * scale;
        state.ScaleY += noted.Y * scale;
        var scaledX = (int)Math.Round(state.ScaleX, MidpointRounding.AwayFromZero);
        var scaledY = (int)Math.Round(state.ScaleY, MidpointRounding.AwayFromZero);
        state.ScaleX -= scaledX;
        state.ScaleY -= scaledY;
        var (turnedX, turnedY) = state.Rotation.Turn(scaledX, scaledY, motion.Rotation);
        var correctionX = turnedX - noted.X;
        var correctionY = turnedY - noted.Y;
        if (correctionX == 0 && correctionY == 0)
            return null;

        return (correctionX, correctionY);
    }

    /// <summary>
    /// The extra wheel delta that makes this device's factor, divided by the global one, what the two turns add up
    /// to. Null when the device sets no factor, the global factor is 0, or the difference rounds to nothing.
    /// </summary>
    public int? CorrectWheel(PointerDevices devices, string device, int delta, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(device);
        if (devices.GlobalScrollStops || !devices.TryScrollFactor(device, out var factor))
            return null;

        var ratio = (factor / devices.ScrollBaseline) - 1;
        if (ratio == 0)
            return null;

        var state = StateFor(device);
        ref var rest = ref horizontal ? ref state.WheelX : ref state.WheelY;
        rest += delta * ratio;
        var whole = (int)Math.Round(rest, MidpointRounding.AwayFromZero);
        rest -= whole;
        return whole == 0 ? null : whole;
    }

    public void Clear()
    {
        _moves.Clear();
        _states.Clear();
    }

    /// <summary>Whether the hook should remember a move for the raw packet. A move it took, or a touchpad's, is not.</summary>
    public static bool ShouldNote(bool defers, bool redirected, bool touchpad, int dx, int dy) =>
        defers && !redirected && !touchpad && (dx != 0 || dy != 0);

    private State StateFor(string device)
    {
        var name = InputDeviceNames.Internal(device);
        if (!_states.TryGetValue(name, out var state))
        {
            state = new State();
            _states.Add(name, state);
        }

        return state;
    }

    private readonly record struct Noted(int X, int Y, long At);

    private sealed class State
    {
        public PointerRotation Rotation { get; } = new();

        public double ScaleX;

        public double ScaleY;

        public double WheelX;

        public double WheelY;
    }
}
