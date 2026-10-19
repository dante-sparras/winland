namespace Winland;

public sealed record ConfigGap(string Keyword, string Reason);

public static class ConfigGaps
{
    public static IReadOnlyList<ConfigGap> All { get; } =
    [
        new("acrylic", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("allow_tearing", "Tearing is a compositor feature with no Windows meaning."),
        new("animation:fadeDim", "Dimming is part of a window's alpha, so fadeSwitch animates it."),
        new("animation:fadeLayers", "Layer shell surfaces have no Windows meaning."),
        new("animation:fadeLayersIn", "Layer shell surfaces have no Windows meaning."),
        new("animation:fadeLayersOut", "Layer shell surfaces have no Windows meaning."),
        new("animation:fadePopups", "Wayland popups have no Windows meaning."),
        new("animation:fadePopupsIn", "Wayland popups have no Windows meaning."),
        new("animation:fadePopupsOut", "Wayland popups have no Windows meaning."),
        new("animation:layers", "Layer shell surfaces have no Windows meaning."),
        new("animation:layersIn", "Layer shell surfaces have no Windows meaning."),
        new("animation:layersOut", "Layer shell surfaces have no Windows meaning."),
        new("animation:monitorAdded", "A monitor that is added does not animate."),
        new("aurora", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("binds:disable_keybind_grabbing", "Applications cannot ask Windows to turn binds off, so there is nothing to refuse."),
        new("bitdepth", "Changing a monitor's bit depth is not honored."),
        new("cursor:enable_hyprcursor", "The cursor theme format has no Windows meaning."),
        new("cursor:min_refresh_rate", "Cursor refresh rate has no Windows meaning."),
        new("cursor:no_break_fs_vrr", "Pausing frames while the cursor moves has no Windows meaning."),
        new("cursor:no_hardware_cursors", "Hardware cursors have no Windows meaning."),
        new("cursor:sync_gsettings_theme", "Syncing the cursor theme with the desktop has no Windows meaning."),
        new("cursor:use_cpu_buffer", "Hardware cursor buffers have no Windows meaning."),
        new("cursor:zoom_disable_aa", "The system magnifier offers plain or edge-smoothed scaling, not unfiltered pixels."),
        new("debug", "Compositor debug options have no Windows meaning."),
        new("decoration:blur:brightness", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:contrast", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:ignore_opacity", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:input_methods", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:input_methods_ignorealpha", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:new_optimizations", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:noise", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:passes", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:popups", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:popups_ignorealpha", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:size", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:special", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:variant", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:vibrancy", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:vibrancy_darkness", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:blur:xray", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("decoration:border_part_of_window", "Whether the border counts as part of the window is not configurable."),
        new("device", "Per-device input is not honored. Honored input settings apply to the whole system."),
        new("dont_inhibit", "Applications cannot inhibit binds, so this flag has nothing to bypass."),
        new("drops", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("ecosystem", "Ecosystem tools have no Windows meaning."),
        new("experimental", "Experimental Wayland protocol options have no Windows meaning."),
        new("fluid_jar", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("glass", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("group:groupbar:blur", "Groupbar blur is not honored."),
        new("haze", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("heat_shimmer", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("input-capture", "The input capture portal has no Windows meaning."),
        new("input:kb_file", "XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant."),
        new("input:kb_model", "XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant."),
        new("input:kb_rules", "XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant."),
        new("input:off_window_axis_events", "Scroll events outside a window are not honored."),
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
        new("input:touchpad:middle_button_emulation", "A button press reaches the mouse hook without its device, so a touchpad's buttons cannot be told from a mouse's, and Windows has no such touchpad setting."),
        new("input:virtualkeyboard:release_pressed_on_close", "Virtual keyboards are not honored."),
        new("input:virtualkeyboard:share_states", "Virtual keyboards are not honored."),
        new("layerrule", "Layer shell rules have no Windows meaning."),
        new("layerrulev2", "Layer shell rules have no Windows meaning."),
        new("locale", "Locale override is not honored."),
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
        new("motion_blur", "Motion blur is not honored."),
        new("opengl", "GPU rendering options have no Windows meaning."),
        new("permission", "Permission rules have no Windows meaning."),
        new("quirks", "Display buffer quirks have no Windows meaning."),
        new("render", "GPU rendering options have no Windows meaning."),
        new("ripple", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("screen_shader", "Screen shaders have no Windows meaning."),
        new("vrr", "Windows has no switch for a display's adaptive sync, since each game turns VRR on for itself. The Variable refresh rate graphics setting only lets DirectX 11 fullscreen games without VRR of their own use it, and Dynamic refresh rate has no documented interface."),
        new("water", "Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned."),
        new("wobble", "DWM cannot deform a window, so wobble is not honored."),
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

    internal static bool IsGap(string keyword)
    {
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
