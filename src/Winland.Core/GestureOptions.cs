namespace Winland;

/// <summary>The <c>gestures</c> options that steer a workspace swipe. Distances are pixels of touchpad travel.</summary>
public sealed record GestureOptions
{
    public int Distance { get; init; } = 300;

    public bool Invert { get; init; } = true;

    public int MinSpeedToForce { get; init; } = 30;

    public double CancelRatio { get; init; } = 0.5;

    public bool CreateNew { get; init; } = true;

    public bool DirectionLock { get; init; } = true;

    public int DirectionLockThreshold { get; init; } = 10;

    public bool Forever { get; init; }

    public bool UseRelative { get; init; }

    public static GestureOptions Documented { get; } = new();

    internal static OptionSet<GestureOptions> Options { get; } = new(
    [
        Of("gestures:workspace_swipe_distance", OptionValue.Int(0, 2000), o => o.Distance, (o, v) => o with { Distance = v }),
        Of("gestures:workspace_swipe_invert", OptionValue.Bool, o => o.Invert, (o, v) => o with { Invert = v }),
        Of("gestures:workspace_swipe_min_speed_to_force", OptionValue.Int(0, 200), o => o.MinSpeedToForce, (o, v) => o with { MinSpeedToForce = v }),
        Of("gestures:workspace_swipe_cancel_ratio", OptionValue.Number(0, 1), o => o.CancelRatio, (o, v) => o with { CancelRatio = v }),
        Of("gestures:workspace_swipe_create_new", OptionValue.Bool, o => o.CreateNew, (o, v) => o with { CreateNew = v }),
        Of("gestures:workspace_swipe_direction_lock", OptionValue.Bool, o => o.DirectionLock, (o, v) => o with { DirectionLock = v }),
        Of("gestures:workspace_swipe_direction_lock_threshold", OptionValue.Int(0, 200), o => o.DirectionLockThreshold, (o, v) => o with { DirectionLockThreshold = v }),
        Of("gestures:workspace_swipe_forever", OptionValue.Bool, o => o.Forever, (o, v) => o with { Forever = v }),
        Of("gestures:workspace_swipe_use_r", OptionValue.Bool, o => o.UseRelative, (o, v) => o with { UseRelative = v }),
    ]);

    public static GestureOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<GestureOptions> Of<T>(string name, OptionValue<T> value, Func<GestureOptions, T> get, Func<GestureOptions, T, GestureOptions> set) =>
        Option<GestureOptions>.Of(name, value, get, set);
}
