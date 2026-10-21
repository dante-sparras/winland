namespace Winland.Core;

public sealed record CursorBehavior(bool HideOnKeyPress, double InactiveTimeoutSeconds)
{
    /// <summary><c>cursor:invisible</c>: the cursor is never shown.</summary>
    public bool Invisible { get; init; }

    /// <summary><c>cursor:warp_back_after_non_mouse_input</c>: a mouse move after another input moved the cursor starts where the mouse left it.</summary>
    public bool WarpBackAfterNonMouseInput { get; init; }

    /// <summary><c>cursor:hide_on_touch</c>: touch hides the cursor until the mouse moves.</summary>
    public bool HideOnTouch { get; init; } = true;

    /// <summary><c>cursor:hide_on_tablet</c>: a pen hides the cursor until the mouse moves.</summary>
    public bool HideOnTablet { get; init; }

    /// <summary><c>cursor:hotspot_padding</c>: how many logical pixels the cursor keeps from the outer edges of the monitors.</summary>
    public int HotspotPadding { get; init; }
}

public sealed record CursorOptions
{
    public int WarpOnWorkspace { get; init; }

    // -1 follows WarpOnWorkspace. Winland does not define this key; later modes are accepted.
    public int WarpOnMonitor { get; init; } = -1;

    public int WarpOnSpecial { get; init; }

    public bool NoWarps { get; init; }

    public bool PersistentWarps { get; init; }

    public double InactiveTimeoutSeconds { get; init; }

    public bool HideOnKeyPress { get; init; }

    public bool Invisible { get; init; }

    public bool WarpBackAfterNonMouseInput { get; init; }

    public bool HideOnTouch { get; init; } = true;

    public bool HideOnTablet { get; init; }

    public string DefaultMonitor { get; init; } = "";

    public int HotspotPadding { get; init; }

    public static CursorOptions Documented { get; } = new();

    public int MonitorMode() => WarpOnMonitor < 0 ? WarpOnWorkspace : WarpOnMonitor;

    public CursorBehavior Behavior() => new(HideOnKeyPress, InactiveTimeoutSeconds)
    {
        Invisible = Invisible,
        WarpBackAfterNonMouseInput = WarpBackAfterNonMouseInput,
        HideOnTouch = HideOnTouch,
        HideOnTablet = HideOnTablet,
        HotspotPadding = HotspotPadding,
    };

    private static readonly Dictionary<string, int> WorkspaceModes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disable"] = 0,
        ["enable"] = 1,
        ["force"] = 2,
    };

    private static readonly Dictionary<string, int> MonitorModes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["same_as_warp_on_change_workspace"] = -1,
        ["disable"] = 0,
        ["enable"] = 1,
        ["force"] = 2,
    };

    internal static OptionSet<CursorOptions> Options { get; } = new(
    [
        Of("cursor:warp_on_change_workspace", OptionValue.Int(0, 2, WorkspaceModes), o => o.WarpOnWorkspace, (o, v) => o with { WarpOnWorkspace = v }),
        Of("cursor:warp_on_monitor_change", OptionValue.Int(-1, 2, MonitorModes), o => o.WarpOnMonitor, (o, v) => o with { WarpOnMonitor = v }),
        Of("cursor:warp_on_toggle_special", OptionValue.Int(0, 2, WorkspaceModes), o => o.WarpOnSpecial, (o, v) => o with { WarpOnSpecial = v }),
        Of("cursor:no_warps", OptionValue.Bool, o => o.NoWarps, (o, v) => o with { NoWarps = v }),
        Of("cursor:persistent_warps", OptionValue.Bool, o => o.PersistentWarps, (o, v) => o with { PersistentWarps = v }),
        Of("cursor:inactive_timeout", OptionValue.Number(0, 20), o => o.InactiveTimeoutSeconds, (o, v) => o with { InactiveTimeoutSeconds = v }),
        Of("cursor:warp_back_after_non_mouse_input", OptionValue.Bool, o => o.WarpBackAfterNonMouseInput, (o, v) => o with { WarpBackAfterNonMouseInput = v }),
        Of("cursor:hide_on_touch", OptionValue.Bool, o => o.HideOnTouch, (o, v) => o with { HideOnTouch = v }),
        Of("cursor:hide_on_tablet", OptionValue.Bool, o => o.HideOnTablet, (o, v) => o with { HideOnTablet = v }),
        Of("cursor:invisible", OptionValue.Bool, o => o.Invisible, (o, v) => o with { Invisible = v }),
        Of("cursor:hide_on_key_press", OptionValue.Bool, o => o.HideOnKeyPress, (o, v) => o with { HideOnKeyPress = v }),
        Of("cursor:default_monitor", OptionValue.Text, o => o.DefaultMonitor, (o, v) => o with { DefaultMonitor = v }),
        Of("cursor:hotspot_padding", OptionValue.Int(0, 20), o => o.HotspotPadding, (o, v) => o with { HotspotPadding = v }),
    ]);

    public static CursorOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<CursorOptions> Of<T>(string name, OptionValue<T> value, Func<CursorOptions, T> get, Func<CursorOptions, T, CursorOptions> set) =>
        Option<CursorOptions>.Of(name, value, get, set);
}
