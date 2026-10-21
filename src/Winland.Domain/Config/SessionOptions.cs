namespace Winland.Domain;

public sealed record SessionOptions
{
    public bool FocusOnActivate { get; init; }

    /// <summary><c>general:modal_parent_blocking</c>: a window shadowed by a modal takes no focus.</summary>
    public bool ModalParentBlocking { get; init; } = true;

    /// <summary><c>general:tray_icon</c>: the notification area shows the focused Workspace's number.</summary>
    public bool ShowTray { get; init; } = true;

    public bool CloseSpecialOnEmpty { get; init; } = true;

    public bool HideSpecialOnWorkspaceChange { get; init; }

    public bool MouseMoveFocusesMonitor { get; init; } = true;

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

    /// <summary>
    /// <c>general:hide_taskbar</c>: hide the taskbars and set them to auto-hide, so Clients tile into the strip they reserved.
    /// </summary>
    public bool HideTaskbar { get; init; }

    /// <summary><c>binds:movefocus_cycles_fullscreen</c>: movefocus from a fullscreen Client cycles its Workspace's windows instead of looking in the direction.</summary>
    public bool MoveFocusCyclesFullscreen { get; init; }

    /// <summary><c>misc:mouse_move_enables_dpms</c>: a pointer move or the start of a swipe turns the displays back on after dpms off.</summary>
    public bool MouseMoveEnablesDpms { get; init; }

    /// <summary><c>misc:key_press_enables_dpms</c>: a key press or release turns the displays back on after dpms off.</summary>
    public bool KeyPressEnablesDpms { get; init; }

    /// <summary><c>misc:font_family</c>: the font of the text Winland draws, where nothing names another.</summary>
    public string FontFamily { get; init; } = "Sans";

    /// <summary><c>misc:animate_mouse_windowdragging</c>: read and kept. A dragged window follows the pointer at once.</summary>
    public bool AnimateMouseWindowDragging { get; init; }

    public static SessionOptions Documented { get; } = new();

    private static readonly Dictionary<string, int> UnderFullscreenNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ignore"] = 0,
        ["take_over"] = 1,
        ["exit_fullscreen"] = 2,
    };

    private static readonly Dictionary<string, int> RetainsFullscreenNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["disable"] = 0,
        ["enable"] = 1,
        ["only_when_grouped"] = 2,
        ["only_when_nongrouped"] = 3,
    };

    // 0 to 3, a name for one, or a boolean for 0 and 1.
    private static readonly OptionValue<int> RetainsFullscreen = OptionValue.Of(
        "int",
        (string text, out int mode) =>
        {
            if (OptionValue.Int(0, 3, RetainsFullscreenNames).Read(text, out mode))
                return true;

            if (!ConfigValueSyntax.TryParseBool(text.Trim(), out var on))
                return false;

            mode = on ? 1 : 0;
            return true;
        },
        OptionValue.Int().Json);

    internal static OptionSet<SessionOptions> Options { get; } = new(
    [
        Of("general:modal_parent_blocking", OptionValue.Bool, o => o.ModalParentBlocking, (o, v) => o with { ModalParentBlocking = v }),
        Of(TrayIcon.Keyword, OptionValue.Bool, o => o.ShowTray, (o, v) => o with { ShowTray = v }),
        Of("misc:focus_on_activate", OptionValue.Bool, o => o.FocusOnActivate, (o, v) => o with { FocusOnActivate = v }),
        Of("misc:close_special_on_empty", OptionValue.Bool, o => o.CloseSpecialOnEmpty, (o, v) => o with { CloseSpecialOnEmpty = v }),
        Of("binds:hide_special_on_workspace_change", OptionValue.Bool, o => o.HideSpecialOnWorkspaceChange, (o, v) => o with { HideSpecialOnWorkspaceChange = v }),
        Of("misc:mouse_move_focuses_monitor", OptionValue.Bool, o => o.MouseMoveFocusesMonitor, (o, v) => o with { MouseMoveFocusesMonitor = v }),
        Of("misc:initial_workspace_tracking", OptionValue.Int(0, 2), o => o.InitialWorkspaceTracking, (o, v) => o with { InitialWorkspaceTracking = v }),
        Of("misc:initial_workspace_token_timeout", OptionValue.Int(1, 3600), o => o.InitialWorkspaceTokenTimeout, (o, v) => o with { InitialWorkspaceTokenTimeout = v }),
        Of("misc:disable_autoreload", OptionValue.Bool, o => o.DisableAutoreload, (o, v) => o with { DisableAutoreload = v }),
        Of("misc:on_focus_under_fullscreen", OptionValue.Int(0, 2, UnderFullscreenNames), o => o.OnFocusUnderFullscreen, (o, v) => o with { OnFocusUnderFullscreen = v }),
        Of("misc:exit_window_retains_fullscreen", RetainsFullscreen, o => o.ExitWindowRetainsFullscreen, (o, v) => o with { ExitWindowRetainsFullscreen = v }),
        Of("binds:allow_pin_fullscreen", OptionValue.Bool, o => o.AllowPinFullscreen, (o, v) => o with { AllowPinFullscreen = v }),
        Of("misc:animate_manual_resizes", OptionValue.Bool, o => o.AnimateManualResizes, (o, v) => o with { AnimateManualResizes = v }),
        Of("misc:animate_mouse_windowdragging", OptionValue.Bool, o => o.AnimateMouseWindowDragging, (o, v) => o with { AnimateMouseWindowDragging = v }),
        Of("general:no_focus_fallback", OptionValue.Bool, o => o.NoFocusFallback, (o, v) => o with { NoFocusFallback = v }),
        Of("general:hide_taskbar", OptionValue.Bool, o => o.HideTaskbar, (o, v) => o with { HideTaskbar = v }),
        Of("binds:movefocus_cycles_fullscreen", OptionValue.Bool, o => o.MoveFocusCyclesFullscreen, (o, v) => o with { MoveFocusCyclesFullscreen = v }),
        Of("misc:mouse_move_enables_dpms", OptionValue.Bool, o => o.MouseMoveEnablesDpms, (o, v) => o with { MouseMoveEnablesDpms = v }),
        Of("misc:key_press_enables_dpms", OptionValue.Bool, o => o.KeyPressEnablesDpms, (o, v) => o with { KeyPressEnablesDpms = v }),
        Of("misc:font_family", OptionValue.Text, o => o.FontFamily, (o, v) => o with { FontFamily = v }),
        Of("misc:size_limits_tiled", OptionValue.Bool, o => o.SizeLimitsTiled, (o, v) => o with { SizeLimitsTiled = v }),
    ]);

    public static SessionOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<SessionOptions> Of<T>(string name, OptionValue<T> value, Func<SessionOptions, T> get, Func<SessionOptions, T, SessionOptions> set) =>
        Option<SessionOptions>.Of(name, value, get, set);
}
