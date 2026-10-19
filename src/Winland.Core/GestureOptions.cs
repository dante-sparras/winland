using System.Globalization;

namespace Winland;

/// <summary>The <c>gestures</c> options that steer a workspace swipe. Distances are pixels of touchpad travel, or a touch screen's width.</summary>
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

    /// <summary><c>gestures:workspace_swipe_touch</c>: a finger swiping in from a touch screen's edge switches Workspace.</summary>
    public bool Touch { get; init; }

    /// <summary><c>gestures:workspace_swipe_touch_invert</c>: the touch screen swipe goes the other way.</summary>
    public bool TouchInvert { get; init; }

    /// <summary><c>gestures:scrolling:move_snap_to_grid</c>: a scrollMove swipe settles on the nearest column.</summary>
    public bool MoveSnapToGrid { get; init; } = true;

    /// <summary><c>gestures:scrolling:move_snap_cursor</c>: the cursor goes to the window a scrollMove swipe focuses.</summary>
    public bool MoveSnapCursor { get; init; } = true;

    /// <summary><c>gestures:close_max_timeout</c>: how long a window the close gesture closed may stay open, in milliseconds.</summary>
    public int CloseMaxTimeout { get; init; } = 1000;

    public static GestureOptions Documented { get; } = new();

    // Adding an entry here applies that keyword and stops the config warning.
    private static readonly Dictionary<string, Func<GestureOptions, string, GestureOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["gestures:workspace_swipe_distance"] = (options, value) => TryInt(value, 0, 2000, out var distance) ? options with { Distance = distance } : options,
        ["gestures:workspace_swipe_invert"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var invert) ? options with { Invert = invert } : options,
        ["gestures:workspace_swipe_min_speed_to_force"] = (options, value) => TryInt(value, 0, 200, out var speed) ? options with { MinSpeedToForce = speed } : options,
        ["gestures:workspace_swipe_cancel_ratio"] = (options, value) => TryFloat(value, 0, 1, out var ratio) ? options with { CancelRatio = ratio } : options,
        ["gestures:workspace_swipe_create_new"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var create) ? options with { CreateNew = create } : options,
        ["gestures:workspace_swipe_direction_lock"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var locked) ? options with { DirectionLock = locked } : options,
        ["gestures:workspace_swipe_direction_lock_threshold"] = (options, value) => TryInt(value, 0, 200, out var threshold) ? options with { DirectionLockThreshold = threshold } : options,
        ["gestures:workspace_swipe_forever"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var forever) ? options with { Forever = forever } : options,
        ["gestures:scrolling:move_snap_to_grid"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var snap) ? options with { MoveSnapToGrid = snap } : options,
        ["gestures:scrolling:move_snap_cursor"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var cursor) ? options with { MoveSnapCursor = cursor } : options,
        ["gestures:close_max_timeout"] = (options, value) => TryInt(value, 10, 2000, out var timeout) ? options with { CloseMaxTimeout = timeout } : options,
        ["gestures:workspace_swipe_touch"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var touch) ? options with { Touch = touch } : options,
        ["gestures:workspace_swipe_touch_invert"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var invert) ? options with { TouchInvert = invert } : options,
        ["gestures:workspace_swipe_use_r"] = (options, value) => ConfigValueSyntax.TryParseBool(value, out var relative) ? options with { UseRelative = relative } : options,
    };

    internal static IReadOnlySet<string> Keywords { get; } = Appliers.Keys.ToHashSet(StringComparer.Ordinal);

    public static GestureOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented;
        foreach (var setting in document.Settings)
        {
            if (Appliers.TryGetValue(setting.Keyword, out var apply))
                options = apply(options, setting.Value);
        }

        return options;
    }

    private static bool TryInt(string text, int min, int max, out int value)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return false;

        value = Math.Clamp(value, min, max);
        return true;
    }

    private static bool TryFloat(string text, double min, double max, out double value)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
            return false;

        value = Math.Clamp(value, min, max);
        return true;
    }
}
