namespace Winland.Core;

/// <summary>The <c>binds:</c> options that steer focus, Workspace switches and pointer binds.</summary>
public sealed record BindOptions
{
    public static BindOptions Documented { get; } = new();

    /// <summary><c>binds:window_direction_monitor_fallback</c>: a direction past a Monitor's edge goes on to the next Monitor.</summary>
    public bool MonitorFallback { get; init; } = true;

    /// <summary><c>binds:focus_preferred_method</c>: 0 picks the tile focused most recently, 1 the one sharing the longest edge.</summary>
    public int FocusPreferredMethod { get; init; }

    /// <summary><c>binds:workspace_back_and_forth</c>: 0 off, 1 the previous Workspace on any Monitor, 2 the previous one on this Monitor.</summary>
    public int BackAndForth { get; init; }

    /// <summary><c>binds:allow_workspace_cycles</c>: the Workspace visited last is recorded again when it is visited again.</summary>
    public bool AllowWorkspaceCycles { get; init; }

    /// <summary><c>binds:drag_center_window</c>: a dragged tile is held by its middle when it is lifted, or by the point grabbed.</summary>
    public bool DragCenterWindow { get; init; } = true;

    /// <summary>
    /// <c>binds:workspace_center_on</c>: switching to a Workspace on another Monitor puts the cursor in the middle of that
    /// Monitor (0) or of the Client it focuses (1).
    /// </summary>
    public int WorkspaceCenterOn { get; init; } = 1;

    /// <summary><c>binds:pass_mouse_when_bound</c>: a mouse button that runs a bind still reaches the window under the pointer.</summary>
    public bool PassMouseWhenBound { get; init; }

    /// <summary><c>binds:drag_threshold</c>: how far the pointer moves, in pixels, before a press becomes a drag. 0 drags at once.</summary>
    public int DragThreshold { get; init; }

    /// <summary><c>binds:scroll_event_delay</c>: milliseconds after a wheel bind runs before the next wheel turn can run one.</summary>
    public int ScrollEventDelay { get; init; } = 300;

    private static readonly Dictionary<string, int> BackAndForthNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disabled"] = 0,
        ["all_monitors"] = 1,
        ["per_monitor"] = 2,
    };

    // 0, 1 or 2, a name for one, or a boolean as older configs write it.
    private static readonly OptionValue<int> BackAndForthMode = OptionValue.Of(
        "int",
        (string text, out int mode) =>
        {
            if (OptionValue.Int(0, 2, BackAndForthNames).Read(text, out mode))
                return true;

            if (!ConfigValueSyntax.TryParseBool(text.Trim(), out var on))
                return false;

            mode = on ? 1 : 0;
            return true;
        },
        OptionValue.Int().Json);

    internal static OptionSet<BindOptions> Options { get; } = new(
    [
        Of("binds:window_direction_monitor_fallback", OptionValue.Bool, o => o.MonitorFallback, (o, v) => o with { MonitorFallback = v }),
        Of("binds:focus_preferred_method", OptionValue.Int(0, 1), o => o.FocusPreferredMethod, (o, v) => o with { FocusPreferredMethod = v }),
        Of("binds:workspace_back_and_forth", BackAndForthMode, o => o.BackAndForth, (o, v) => o with { BackAndForth = v }),
        Of("binds:allow_workspace_cycles", OptionValue.Bool, o => o.AllowWorkspaceCycles, (o, v) => o with { AllowWorkspaceCycles = v }),
        Of("binds:drag_center_window", OptionValue.Bool, o => o.DragCenterWindow, (o, v) => o with { DragCenterWindow = v }),
        Of("binds:workspace_center_on", OptionValue.Int(0, 1), o => o.WorkspaceCenterOn, (o, v) => o with { WorkspaceCenterOn = v }),
        Of("binds:pass_mouse_when_bound", OptionValue.Bool, o => o.PassMouseWhenBound, (o, v) => o with { PassMouseWhenBound = v }),
        Of("binds:drag_threshold", OptionValue.Int(0), o => o.DragThreshold, (o, v) => o with { DragThreshold = v }),
        Of("binds:scroll_event_delay", OptionValue.Int(0, 2000), o => o.ScrollEventDelay, (o, v) => o with { ScrollEventDelay = v }),
    ]);

    public static BindOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<BindOptions> Of<T>(string name, OptionValue<T> value, Func<BindOptions, T> get, Func<BindOptions, T, BindOptions> set) =>
        Option<BindOptions>.Of(name, value, get, set);
}
