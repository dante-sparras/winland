namespace Winland;

public sealed record ConfigGap(string Keyword, string Reason);

public static class ConfigGaps
{
    public static IReadOnlyList<ConfigGap> All { get; } =
    [
        new("allow_tearing", "Tearing is a compositor feature with no Windows meaning."),
        new("animation", "Animations are not honored."),
        new("animations", "Animations are not honored."),
        new("bezier", "Animations are not honored."),
        new("binds:allow_pin_fullscreen", "Fullscreen does not keep or restore a pin."),
        new("binds:movefocus_cycles_fullscreen", "Moving focus does not cycle a fullscreen Client."),
        new("bitdepth", "Changing a monitor's bit depth is not honored."),
        new("blur", "Blur is not honored."),
        new("center_ignores_reserved", "Reserved monitor regions are not separate from the work area, so the master area cannot ignore them."),
        new("cm", "Monitor color management is not honored."),
        new("cursor:enable_hyprcursor", "The cursor theme format has no Windows meaning."),
        new("cursor:hide_on_tablet", "Tablet input is not honored."),
        new("cursor:hide_on_touch", "Touch input is not honored."),
        new("cursor:hotspot_padding", "Cursor edge padding is not honored."),
        new("cursor:invisible", "Always hiding the cursor is not honored."),
        new("cursor:min_refresh_rate", "Cursor refresh rate has no Windows meaning."),
        new("cursor:no_break_fs_vrr", "Pausing frames while the cursor moves has no Windows meaning."),
        new("cursor:no_hardware_cursors", "Hardware cursors have no Windows meaning."),
        new("cursor:sync_gsettings_theme", "Syncing the cursor theme with the desktop has no Windows meaning."),
        new("cursor:use_cpu_buffer", "Hardware cursor buffers have no Windows meaning."),
        new("cursor:warp_back_after_non_mouse_input", "Returning the cursor after non-mouse input is not honored."),
        new("cursor:zoom_detached_camera", "Cursor zoom is not honored."),
        new("cursor:zoom_disable_aa", "Cursor zoom is not honored."),
        new("cursor:zoom_factor", "Cursor zoom is not honored."),
        new("cursor:zoom_rigid", "Cursor zoom is not honored."),
        new("debug", "Compositor debug options have no Windows meaning."),
        new("device", "Per-device input is not honored. Honored input settings apply to the whole system."),
        new("dim_around", "Dimming the area around a window is not honored."),
        new("dont_inhibit", "Applications cannot inhibit binds, so this flag has nothing to bypass."),
        new("ecosystem", "Ecosystem tools have no Windows meaning."),
        new("gaps_workspaces", "Workspace swipe gaps are not honored."),
        new("gestures:close_max_timeout", "The close gesture is not honored, so its timeout has nothing to time."),
        new("gestures:workspace_swipe_touch", "Touchscreen input is not honored, so a touchscreen edge swipe does not switch workspaces."),
        new("gestures:workspace_swipe_touch_invert", "Touchscreen input is not honored, so a touchscreen edge swipe does not switch workspaces."),
        new("gradient", "Gradient borders are not honored."),
        new("group", "Groups are not honored."),
        new("input:emulate_discrete_scroll", "High resolution scroll emulation is not honored."),
        new("input:force_no_accel", "Forcing acceleration off apart from the acceleration profile is not honored."),
        new("input:kb_file", "Keyboard layout is not changed."),
        new("input:kb_layout", "Keyboard layout is not changed."),
        new("input:kb_model", "Keyboard layout is not changed."),
        new("input:kb_options", "Keyboard layout is not changed."),
        new("input:kb_rules", "Keyboard layout is not changed."),
        new("input:kb_variant", "Keyboard layout is not changed."),
        new("input:off_window_axis_events", "Scroll events outside a window are not honored."),
        new("input:resolve_binds_by_sym", "Binds are not resolved by key symbol."),
        new("input:rotation", "Device rotation is not honored."),
        new("input:scroll_button", "Touchpad scroll button has no Windows meaning."),
        new("input:scroll_button_lock", "Touchpad scroll button lock has no Windows meaning."),
        new("input:scroll_method", "Touchpad scroll method has no Windows meaning."),
        new("input:scroll_points", "Custom scroll curves are not honored."),
        new("input:tablet:absolute_region_position", "Tablets are not honored."),
        new("input:tablet:active_area_position", "Tablets are not honored."),
        new("input:tablet:active_area_size", "Tablets are not honored."),
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
        new("input:touchdevice:enabled", "Touch screens are not honored."),
        new("input:touchdevice:output", "Touch screens are not honored."),
        new("input:touchdevice:transform", "Touch screens are not honored."),
        new("input:touchpad:clickfinger_behavior", "Touchpad options are not honored."),
        new("input:touchpad:disable_while_typing", "Touchpad options are not honored."),
        new("input:touchpad:drag_3fg", "Touchpad options are not honored."),
        new("input:touchpad:drag_lock", "Touchpad options are not honored."),
        new("input:touchpad:flip_x", "Touchpad options are not honored."),
        new("input:touchpad:flip_y", "Touchpad options are not honored."),
        new("input:touchpad:middle_button_emulation", "Touchpad options are not honored."),
        new("input:touchpad:natural_scroll", "Touchpad scroll direction is not honored."),
        new("input:touchpad:scroll_factor", "Touchpad options are not honored."),
        new("input:touchpad:tap-and-drag", "Touchpad options are not honored."),
        new("input:touchpad:tap-to-click", "Touchpad options are not honored."),
        new("input:touchpad:tap_button_map", "Touchpad options are not honored."),
        new("input:virtualkeyboard:release_pressed_on_close", "Virtual keyboards are not honored."),
        new("input:virtualkeyboard:share_states", "Virtual keyboards are not honored."),
        new("layerrule", "Layer shell rules have no Windows meaning."),
        new("layerrulev2", "Layer shell rules have no Windows meaning."),
        new("locale", "Locale override is not honored."),
        new("mirror", "Mirroring a monitor is not honored."),
        new("misc:exit_window_retains_fullscreen", "Closing a fullscreen Client does not pass fullscreen to the next Client."),
        new("misc:on_focus_under_fullscreen", "Focusing a Client under a fullscreen one does not take over or leave fullscreen."),
        new("mode", "Changing a monitor's resolution and refresh rate is not honored."),
        new("modal_parent_blocking", "Modal parent blocking is not honored."),
        new("move_snap_cursor", "Trackpad scroll-move cursor snapping is a gesture, not part of the layout."),
        new("move_snap_to_grid", "Trackpad scroll-move snapping is a gesture, not part of the layout."),
        new("no_focus_fallback", "Focus fallback is not honored."),
        new("opengl", "GPU rendering options have no Windows meaning."),
        new("permission", "Permission rules have no Windows meaning."),
        new("quirks", "Display buffer quirks have no Windows meaning."),
        new("render", "GPU rendering options have no Windows meaning."),
        new("scale", "Changing a monitor's scale is not honored."),
        new("screen_shader", "Screen shaders have no Windows meaning."),
        new("shadow", "Shadows are not honored."),
        new("special_scale_factor", "The master layout does not scale a special workspace."),
        new("vrr", "Variable refresh rate is not honored."),
        new("xwayland", "X11 compatibility has no Windows meaning. A window rule that matches on xwayland is not honored."),
    ];

    private static readonly HashSet<string> Keywords = new(All.Select(gap => gap.Keyword), StringComparer.Ordinal);

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
