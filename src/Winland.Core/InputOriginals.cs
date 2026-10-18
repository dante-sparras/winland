namespace Winland;

public readonly record struct MouseCurve(int Threshold1, int Threshold2, int Acceleration);

public sealed record InputOriginals
{
    public int? KeyboardDelay { get; init; }

    public int? KeyboardSpeed { get; init; }

    public bool? NumLock { get; init; }

    public bool? SwappedButtons { get; init; }

    public MouseCurve? MouseCurve { get; init; }

    public int? MouseSpeed { get; init; }

    public int? WheelLines { get; init; }

    public TouchpadSettings? Touchpad { get; init; }

    public bool Any =>
        KeyboardDelay is not null
        || KeyboardSpeed is not null
        || NumLock is not null
        || SwappedButtons is not null
        || MouseCurve is not null
        || MouseSpeed is not null
        || WheelLines is not null
        || Touchpad is not null;
}
