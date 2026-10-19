using static Winland.OrdinalText;

namespace Winland;

public sealed record SessionOptions
{
    public bool FocusOnActivate { get; init; }

    /// <summary><c>general:modal_parent_blocking</c>: a window shadowed by a modal takes no focus.</summary>
    public bool ModalParentBlocking { get; init; } = true;

    public bool CloseSpecialOnEmpty { get; init; } = true;

    public bool HideSpecialOnWorkspaceChange { get; init; }

    public bool MouseMoveFocusesMonitor { get; init; } = true;

    public bool EnableSwallow { get; init; }

    /// <summary>The class a parent Client must fully match to be swallowed. Empty swallows nothing.</summary>
    public string SwallowRegex { get; init; } = "";

    /// <summary>The title that keeps a parent Client from being swallowed. Empty excludes nothing.</summary>
    public string SwallowExceptionRegex { get; init; } = "";

    /// <summary>Whether the minimum and maximum size window rules also hold a tiled Client, not only a floating one.</summary>
    public bool SizeLimitsTiled { get; init; }

    /// <summary><c>misc:disable_autoreload</c>: saving the config does not reload it.</summary>
    public bool DisableAutoreload { get; init; }

    /// <summary><c>misc:initial_workspace_tracking</c>: 0 off, 1 the first window of a started program, 2 all of them.</summary>
    public int InitialWorkspaceTracking { get; init; } = 1;

    /// <summary><c>misc:initial_workspace_token_timeout</c>: seconds a started program has to open its window, with tracking 1.</summary>
    public int InitialWorkspaceTokenTimeout { get; init; } = 10;

    /// <summary><c>misc:on_focus_under_fullscreen</c>: 0 keeps the fullscreen Client focused, 1 hands it over, 2 ends it.</summary>
    public int OnFocusUnderFullscreen { get; init; } = 2;

    /// <summary><c>misc:exit_window_retains_fullscreen</c>: 0 off, 1 always, 2 only within a Group, 3 only outside one.</summary>
    public int ExitWindowRetainsFullscreen { get; init; }

    /// <summary><c>binds:allow_pin_fullscreen</c>: a pinned Client can go fullscreen, and is pinned again when it leaves that mode.</summary>
    public bool AllowPinFullscreen { get; init; }

    /// <summary><c>misc:animate_manual_resizes</c>: a dwindle tile resized by a dispatcher animates instead of moving at once.</summary>
    public bool AnimateManualResizes { get; init; }

    /// <summary><c>general:no_focus_fallback</c>: movefocus with nothing in its direction answers an error.</summary>
    public bool NoFocusFallback { get; init; }

    /// <summary><c>binds:movefocus_cycles_fullscreen</c>: movefocus from a fullscreen Client cycles its Workspace's windows instead of looking in the direction.</summary>
    public bool MoveFocusCyclesFullscreen { get; init; }

    /// <summary><c>misc:mouse_move_enables_dpms</c>: a pointer move or the start of a swipe turns the displays back on after dpms off.</summary>
    public bool MouseMoveEnablesDpms { get; init; }

    /// <summary><c>misc:key_press_enables_dpms</c>: a key press or release turns the displays back on after dpms off.</summary>
    public bool KeyPressEnablesDpms { get; init; }

    /// <summary><c>misc:font_family</c>: the font of the text Winland draws, where nothing names another.</summary>
    public string FontFamily { get; init; } = "Sans";

    public static SessionOptions Documented { get; } = new();

    // Adding an entry here applies that keyword and stops the config warning.
    private static readonly Dictionary<string, Func<SessionOptions, string, SessionOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["general:modal_parent_blocking"] = (options, value) => TryParseBool(value, out var blocking) ? options with { ModalParentBlocking = blocking } : options,
        ["misc:focus_on_activate"] = (options, value) => TryParseBool(value, out var focus) ? options with { FocusOnActivate = focus } : options,
        ["misc:close_special_on_empty"] = (options, value) => TryParseBool(value, out var close) ? options with { CloseSpecialOnEmpty = close } : options,
        ["binds:hide_special_on_workspace_change"] = (options, value) => TryParseBool(value, out var hide) ? options with { HideSpecialOnWorkspaceChange = hide } : options,
        ["misc:mouse_move_focuses_monitor"] = (options, value) => TryParseBool(value, out var mouse) ? options with { MouseMoveFocusesMonitor = mouse } : options,
        ["misc:enable_swallow"] = (options, value) => TryParseBool(value, out var swallow) ? options with { EnableSwallow = swallow } : options,
        ["misc:swallow_regex"] = (options, value) => options with { SwallowRegex = Unset(value) },
        ["misc:swallow_exception_regex"] = (options, value) => options with { SwallowExceptionRegex = Unset(value) },
        ["misc:initial_workspace_tracking"] = (options, value) => int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var tracking) ? options with { InitialWorkspaceTracking = Math.Clamp(tracking, 0, 2) } : options,
        ["misc:initial_workspace_token_timeout"] = (options, value) => int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var timeout) ? options with { InitialWorkspaceTokenTimeout = Math.Clamp(timeout, 1, 3600) } : options,
        ["misc:disable_autoreload"] = (options, value) => TryParseBool(value, out var disable) ? options with { DisableAutoreload = disable } : options,
        ["misc:on_focus_under_fullscreen"] = (options, value) => UnderFullscreen(value) is int mode ? options with { OnFocusUnderFullscreen = mode } : options,
        ["misc:exit_window_retains_fullscreen"] = (options, value) => RetainsFullscreen(value) is int retain ? options with { ExitWindowRetainsFullscreen = retain } : options,
        ["binds:allow_pin_fullscreen"] = (options, value) => TryParseBool(value, out var allow) ? options with { AllowPinFullscreen = allow } : options,
        ["misc:animate_manual_resizes"] = (options, value) => TryParseBool(value, out var animate) ? options with { AnimateManualResizes = animate } : options,
        ["general:no_focus_fallback"] = (options, value) => TryParseBool(value, out var none) ? options with { NoFocusFallback = none } : options,
        ["binds:movefocus_cycles_fullscreen"] = (options, value) => TryParseBool(value, out var cycles) ? options with { MoveFocusCyclesFullscreen = cycles } : options,
        ["misc:mouse_move_enables_dpms"] = (options, value) => TryParseBool(value, out var mouse) ? options with { MouseMoveEnablesDpms = mouse } : options,
        ["misc:key_press_enables_dpms"] = (options, value) => TryParseBool(value, out var key) ? options with { KeyPressEnablesDpms = key } : options,
        ["misc:font_family"] = (options, value) => options with { FontFamily = value.Trim() },
        ["misc:size_limits_tiled"] = (options, value) => TryParseBool(value, out var tiled) ? options with { SizeLimitsTiled = tiled } : options,
    };

    private static int? UnderFullscreen(string value) => value.Trim() switch
    {
        "ignore" => 0,
        "take_over" => 1,
        "exit_fullscreen" => 2,
        var number when int.TryParse(number, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var mode) && mode is >= 0 and <= 2 => mode,
        _ => null,
    };

    private static int? RetainsFullscreen(string value) => value.Trim() switch
    {
        "disable" => 0,
        "enable" => 1,
        "only_when_grouped" => 2,
        "only_when_nongrouped" => 3,
        var number when int.TryParse(number, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var mode) && mode is >= 0 and <= 3 => mode,
        var flag when TryParseBool(flag, out var on) => on ? 1 : 0,
        _ => null,
    };

    internal static IReadOnlySet<string> Keywords { get; } = Appliers.Keys.ToHashSet(StringComparer.Ordinal);

    public static SessionOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented;
        foreach (var setting in document.Settings)
            options = Apply(options, setting);
        return options;
    }

    private static SessionOptions Apply(SessionOptions options, Setting setting) =>
        Appliers.TryGetValue(setting.Keyword, out var apply) ? apply(options, setting.Value) : options;

    // The reference config writes an unset string as [[EMPTY]].
    private static string Unset(string value) => value == "[[EMPTY]]" ? "" : value;

    private static bool TryParseBool(string text, out bool value)
    {
        if (Is(text, "true") || Is(text, "yes") || Is(text, "on") || text == "1")
        {
            value = true;
            return true;
        }

        if (Is(text, "false") || Is(text, "no") || Is(text, "off") || text == "0")
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }
}
