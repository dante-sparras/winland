namespace Winland;

public sealed record ConfigGap(string Keyword, string Reason);

public static class ConfigGaps
{
    public static IReadOnlyList<ConfigGap> All { get; } =
    [
        new("acrylic", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("allow_tearing", "Tearing is a compositor feature with no Windows meaning."),
        new("animation:fadeLayers", "Layer shell surfaces have no Windows meaning."),
        new("animation:fadeLayersIn", "Layer shell surfaces have no Windows meaning."),
        new("animation:fadeLayersOut", "Layer shell surfaces have no Windows meaning."),
        new("animation:fadePopups", "Wayland popups have no Windows meaning."),
        new("animation:fadePopupsIn", "Wayland popups have no Windows meaning."),
        new("animation:fadePopupsOut", "Wayland popups have no Windows meaning."),
        new("animation:layers", "Layer shell surfaces have no Windows meaning."),
        new("animation:layersIn", "Layer shell surfaces have no Windows meaning."),
        new("animation:layersOut", "Layer shell surfaces have no Windows meaning."),
        new("animation:monitorAdded", "A monitor that is added zooms its picture and fades its wallpaper in. Windows draws that picture and that wallpaper, so neither can be animated."),
        new("aurora", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("binds:disable_keybind_grabbing", "Applications cannot ask Windows to turn binds off, so there is nothing to refuse."),
        new("cursor:enable_hyprcursor", "The cursor theme format has no Windows meaning."),
        new("cursor:min_refresh_rate", "Cursor refresh rate has no Windows meaning."),
        new("cursor:no_break_fs_vrr", "Pausing frames while the cursor moves has no Windows meaning."),
        new("cursor:no_hardware_cursors", "Hardware cursors have no Windows meaning."),
        new("cursor:sync_gsettings_theme", "Syncing the cursor theme with the desktop has no Windows meaning."),
        new("cursor:use_cpu_buffer", "Hardware cursor buffers have no Windows meaning."),
        new("debug", "Compositor debug options have no Windows meaning. The log and error bar options are honored."),
        new("decoration:blur:input_methods", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:input_methods_ignorealpha", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:new_optimizations", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:popups", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:popups_ignorealpha", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:special", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:variant", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("device:absolute_region_position", "Tablets are not honored."),
        new("device:accel_profile", "Windows has one pointer acceleration curve. Raw motion is in the device's own units, and taking over pointer movement to apply another curve is not reliable."),
        new("device:active_area_position", "Tablets are not honored."),
        new("device:active_area_size", "Tablets are not honored."),
        new("device:clickfinger_behavior", "Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad."),
        new("device:disable_while_typing", "Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad."),
        new("device:drag_3fg", "Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad."),
        new("device:drag_lock", "Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad."),
        new("device:enabled", "Disabling a device node needs administrator rights."),
        new("device:flip_x", "Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad."),
        new("device:flip_y", "Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad."),
        new("device:kb_file", "XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant."),
        new("device:kb_layout", "Windows binds a keyboard layout to the foreground window, not to one keyboard."),
        new("device:kb_model", "XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant."),
        new("device:kb_options", "The keyboard hook applies the option before the keyboard is known."),
        new("device:kb_rules", "XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant."),
        new("device:kb_variant", "Windows binds a keyboard layout to the foreground window, not to one keyboard."),
        new("device:keybinds", "Windows names the keyboard only after the key is delivered, and swallowing the key produces no raw input."),
        new("device:left_handed", "SwapMouseButton swaps the buttons of every mouse, and the click is delivered before raw input names the mouse."),
        new("device:middle_button_emulation", "Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad."),
        new("device:natural_scroll", "natural_scroll only reverses the bind's key. The wheel is matched before the device is known, and a swallowed wheel produces no raw input."),
        new("device:numlock_by_default", "Windows has one Num Lock state."),
        new("device:output", "Windows cannot map touch to one display per device, and tablets are not honored."),
        new("device:region_position", "Tablets are not honored."),
        new("device:region_size", "Tablets are not honored."),
        new("device:relative_input", "Tablets are not honored."),
        new("device:release_pressed_on_close", "Virtual keyboards are not honored."),
        new("device:repeat_delay", "Windows has one key repeat, and a repeat is not a raw packet from one keyboard."),
        new("device:repeat_rate", "Windows has one key repeat, and a repeat is not a raw packet from one keyboard."),
        new("device:resolve_binds_by_sym", "Binds that consume the key cannot tell which keyboard sent it."),
        new("device:scroll_button", "Button scrolling is decided in the mouse hook before the device is known."),
        new("device:scroll_button_lock", "Button scrolling is decided in the mouse hook before the device is known."),
        new("device:scroll_method", "Button scrolling is decided in the mouse hook before the device is known."),
        new("device:scroll_points", "One scroll curve is applied in the mouse hook before the device is known."),
        new("device:share_states", "Virtual keyboards are not honored."),
        new("device:tap-and-drag", "Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad."),
        new("device:tap-to-click", "Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad."),
        new("device:tap_button_map", "Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad."),
        new("device:transform", "Windows cannot map touch to one display per device, and tablets are not honored."),
        new("dont_inhibit", "Applications cannot inhibit binds, so this flag has nothing to bypass."),
        new("drops", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("ecosystem", "Ecosystem tools have no Windows meaning."),
        new("experimental", "Experimental Wayland protocol options have no Windows meaning."),
        new("fluid_jar", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("glass", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("haze", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("heat_shimmer", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("input-capture", "The input capture portal has no Windows meaning."),
        new("input:kb_file", "XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant."),
        new("input:kb_model", "XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant."),
        new("input:kb_rules", "XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant."),
        new("input:tablet:absolute_region_position", "Tablets are not honored."),
        new("input:tablet:active_area_position", "Tablets are not honored."),
        new("input:tablet:active_area_size", "Tablets are not honored."),
        new("input:tablet:enabled", "Tablets are not honored."),
        new("input:tablet:left_handed", "Tablets are not honored."),
        new("input:tablet:output", "Tablets are not honored."),
        new("input:tablet:region_position", "Tablets are not honored."),
        new("input:tablet:region_size", "Tablets are not honored."),
        new("input:tablet:relative_input", "Tablets are not honored."),
        new("input:tablet:transform", "Tablets are not honored."),
        new("input:tablettool:eraser_button_mode", "Tablet tools are not honored."),
        new("input:tablettool:eraser_button_override", "Tablet tools are not honored."),
        new("input:tablettool:pressure_range_max", "Tablet tools are not honored."),
        new("input:tablettool:pressure_range_min", "Tablet tools are not honored."),
        new("input:touchdevice:enabled", "Windows turns a touch screen off only by disabling its device, which needs administrator rights."),
        new("input:touchdevice:output", "Windows maps a touch screen to a display in its Tablet PC settings, which Winland cannot set. The touch screen swipe reads the display Windows maps it to."),
        new("input:touchdevice:transform", "Windows turns touch input with its display's orientation and has no setting to turn it apart from that."),
        new("input:virtualkeyboard:release_pressed_on_close", "Virtual keyboards are not honored."),
        new("input:virtualkeyboard:share_states", "Virtual keyboards are not honored."),
        new("layerrule", "Layer shell rules have no Windows meaning."),
        new("layerrulev2", "Layer shell rules have no Windows meaning."),
        new("locale", "general:locale only selects the language of the program's own translated messages. Winland's messages are English and have no other language, and the option does not change how numbers or dates are written."),
        new("misc:allow_session_lock_restore", "The lock screen belongs to Windows."),
        new("misc:always_follow_on_dnd", "Windows tells no other program that a drag and drop is under way, so focus cannot follow the pointer only then."),
        new("misc:anr_missed_pings", "Windows marks a hung window as not responding itself."),
        new("misc:background_color", "The desktop background belongs to Windows."),
        new("misc:bell_sound", "The system bell sound is set in Windows."),
        new("misc:col.splash", "There is no splash text."),
        new("misc:disable_scale_notification", "Windows sets each monitor's scale, so there is no scale notification."),
        new("misc:disable_splash_rendering", "There is no splash text."),
        new("misc:disable_watchdog_warning", "There is no start watchdog, so there is no warning to turn off."),
        new("misc:disable_xdg_env_checks", "XDG environment variables have no Windows meaning."),
        new("misc:enable_anr_dialog", "Windows marks a hung window as not responding itself."),
        new("misc:force_default_wallpaper", "There is no wallpaper. The desktop background belongs to Windows."),
        new("misc:layers_hog_keyboard_focus", "Layer shell surfaces have no Windows meaning."),
        new("misc:lockdead_screen_delay", "The lock screen belongs to Windows."),
        new("misc:middle_click_paste", "The primary selection has no Windows meaning."),
        new("misc:name_vk_after_proc", "Virtual keyboards are not honored."),
        new("misc:render_unfocused_fps", "Each application renders itself, so its frame rate has no Windows meaning."),
        new("misc:screencopy_force_8b", "Screen copy buffers have no Windows meaning."),
        new("misc:session_lock_blur", "The lock screen belongs to Windows."),
        new("misc:session_lock_xray", "The lock screen belongs to Windows."),
        new("misc:splash_font_family", "There is no splash text."),
        new("opengl", "GPU rendering options have no Windows meaning."),
        new("permission", "Permission rules have no Windows meaning."),
        new("quirks", "Display buffer quirks have no Windows meaning."),
        new("render", "GPU rendering options have no Windows meaning."),
        new("ripple", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("screen_shader", "Screen shaders have no Windows meaning."),
        new("water", "Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned."),
        new("xwayland", "X11 compatibility has no Windows meaning. A window rule that matches on xwayland is not honored."),
    ];

    private static readonly HashSet<string> Keywords = new(All.Select(gap => gap.Keyword), StringComparer.Ordinal);

    internal static string? ReasonFor(string keyword)
    {
        foreach (var gap in All)
        {
            if (gap.Keyword == keyword)
                return gap.Reason;
        }

        return null;
    }

    /// <summary>Whether <paramref name="message"/> is the reason one of the Gaps gives.</summary>
    internal static bool IsReason(string message) => Reasons.Contains(message);

    private static readonly HashSet<string> Reasons = new(All.Select(gap => gap.Reason), StringComparer.Ordinal);

    internal static bool IsGap(string keyword)
    {
        if (DebugOptions.Keywords.Contains(keyword))
            return false;

        if (Keywords.Contains(keyword))
            return true;

        foreach (var segment in keyword.Split(':'))
        {
            if (Keywords.Contains(segment))
                return true;
        }

        return false;
    }
}
