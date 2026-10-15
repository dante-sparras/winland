# Gaps

These keywords are not honored. The config parser warns with the line number, skips the line, and still loads the config. A keyword that is not listed here and is not one Winland reads is skipped with the same warning; it is not a Gap until it is recorded here.

Honored behavior is in [behavior.md](behavior.md), the additions Winland makes to Hyprland are in [extensions.md](extensions.md), and key and scan codes are in [keys.md](keys.md). The terms Gap and Non-goal are defined in [CONTEXT.md](../CONTEXT.md).

## Keywords

This table has one row per keyword. A test checks it against the keywords the config parser skips, so every Gap keyword is a row here.

| keyword | reason |
| --- | --- |
| allow_tearing | Tearing is a compositor feature with no Windows meaning. |
| animation | Animations are not honored. |
| animations | Animations are not honored. |
| bezier | Animations are not honored. |
| binds:allow_pin_fullscreen | Fullscreen does not keep or restore a pin. |
| binds:movefocus_cycles_fullscreen | Moving focus does not cycle a fullscreen Client. |
| bitdepth | Changing a monitor's bit depth is not honored. |
| blur | Blur is not honored. |
| center_ignores_reserved | Reserved monitor regions are not separate from the work area, so the master area cannot ignore them. |
| cm | Monitor color management is not honored. |
| cursor:enable_hyprcursor | The cursor theme format has no Windows meaning. |
| cursor:hide_on_tablet | Tablet input is not honored. |
| cursor:hide_on_touch | Touch input is not honored. |
| cursor:hotspot_padding | Cursor edge padding is not honored. |
| cursor:invisible | Always hiding the cursor is not honored. |
| cursor:min_refresh_rate | Cursor refresh rate has no Windows meaning. |
| cursor:no_break_fs_vrr | Pausing frames while the cursor moves has no Windows meaning. |
| cursor:no_hardware_cursors | Hardware cursors have no Windows meaning. |
| cursor:sync_gsettings_theme | Syncing the cursor theme with the desktop has no Windows meaning. |
| cursor:use_cpu_buffer | Hardware cursor buffers have no Windows meaning. |
| cursor:warp_back_after_non_mouse_input | Returning the cursor after non-mouse input is not honored. |
| cursor:zoom_detached_camera | Cursor zoom is not honored. |
| cursor:zoom_disable_aa | Cursor zoom is not honored. |
| cursor:zoom_factor | Cursor zoom is not honored. |
| cursor:zoom_rigid | Cursor zoom is not honored. |
| debug | Compositor debug options have no Windows meaning. |
| device | Per-device input is not honored. Honored input settings apply to the whole system. |
| dim_around | Dimming the area around a window is not honored. |
| dont_inhibit | Applications cannot inhibit binds, so this flag has nothing to bypass. |
| ecosystem | Ecosystem tools have no Windows meaning. |
| gaps_workspaces | Workspace swipe gaps are not honored. |
| gestures:close_max_timeout | The close gesture is not honored, so its timeout has nothing to time. |
| gestures:workspace_swipe_touch | Touchscreen input is not honored, so a touchscreen edge swipe does not switch workspaces. |
| gestures:workspace_swipe_touch_invert | Touchscreen input is not honored, so a touchscreen edge swipe does not switch workspaces. |
| gradient | Gradient borders are not honored. |
| group | Groups are not honored. |
| input:emulate_discrete_scroll | High resolution scroll emulation is not honored. |
| input:force_no_accel | Forcing acceleration off apart from the acceleration profile is not honored. |
| input:kb_file | Keyboard layout is not changed. |
| input:kb_layout | Keyboard layout is not changed. |
| input:kb_model | Keyboard layout is not changed. |
| input:kb_options | Keyboard layout is not changed. |
| input:kb_rules | Keyboard layout is not changed. |
| input:kb_variant | Keyboard layout is not changed. |
| input:off_window_axis_events | Scroll events outside a window are not honored. |
| input:resolve_binds_by_sym | Binds are not resolved by key symbol. |
| input:rotation | Device rotation is not honored. |
| input:scroll_button | Touchpad scroll button has no Windows meaning. |
| input:scroll_button_lock | Touchpad scroll button lock has no Windows meaning. |
| input:scroll_method | Touchpad scroll method has no Windows meaning. |
| input:scroll_points | Custom scroll curves are not honored. |
| input:tablet:absolute_region_position | Tablets are not honored. |
| input:tablet:active_area_position | Tablets are not honored. |
| input:tablet:active_area_size | Tablets are not honored. |
| input:tablet:left_handed | Tablets are not honored. |
| input:tablet:output | Tablets are not honored. |
| input:tablet:region_position | Tablets are not honored. |
| input:tablet:region_size | Tablets are not honored. |
| input:tablet:relative_input | Tablets are not honored. |
| input:tablet:transform | Tablets are not honored. |
| input:tablettool:eraser_button_mode | Tablet tools are not honored. |
| input:tablettool:eraser_button_override | Tablet tools are not honored. |
| input:tablettool:pressure_range_max | Tablet tools are not honored. |
| input:tablettool:pressure_range_min | Tablet tools are not honored. |
| input:touchdevice:enabled | Touch screens are not honored. |
| input:touchdevice:output | Touch screens are not honored. |
| input:touchdevice:transform | Touch screens are not honored. |
| input:touchpad:clickfinger_behavior | Touchpad options are not honored. |
| input:touchpad:disable_while_typing | Touchpad options are not honored. |
| input:touchpad:drag_3fg | Touchpad options are not honored. |
| input:touchpad:drag_lock | Touchpad options are not honored. |
| input:touchpad:flip_x | Touchpad options are not honored. |
| input:touchpad:flip_y | Touchpad options are not honored. |
| input:touchpad:middle_button_emulation | Touchpad options are not honored. |
| input:touchpad:natural_scroll | Touchpad scroll direction is not honored. |
| input:touchpad:scroll_factor | Touchpad options are not honored. |
| input:touchpad:tap-and-drag | Touchpad options are not honored. |
| input:touchpad:tap-to-click | Touchpad options are not honored. |
| input:touchpad:tap_button_map | Touchpad options are not honored. |
| input:virtualkeyboard:release_pressed_on_close | Virtual keyboards are not honored. |
| input:virtualkeyboard:share_states | Virtual keyboards are not honored. |
| layerrule | Layer shell rules have no Windows meaning. |
| layerrulev2 | Layer shell rules have no Windows meaning. |
| locale | Locale override is not honored. |
| mirror | Mirroring a monitor is not honored. |
| misc:exit_window_retains_fullscreen | Closing a fullscreen Client does not pass fullscreen to the next Client. |
| misc:on_focus_under_fullscreen | Focusing a Client under a fullscreen one does not take over or leave fullscreen. |
| mode | Changing a monitor's resolution and refresh rate is not honored. |
| modal_parent_blocking | Modal parent blocking is not honored. |
| move_snap_cursor | Trackpad scroll-move cursor snapping is a gesture, not part of the layout. |
| move_snap_to_grid | Trackpad scroll-move snapping is a gesture, not part of the layout. |
| no_focus_fallback | Focus fallback is not honored. |
| opengl | GPU rendering options have no Windows meaning. |
| permission | Permission rules have no Windows meaning. |
| quirks | Display buffer quirks have no Windows meaning. |
| render | GPU rendering options have no Windows meaning. |
| scale | Changing a monitor's scale is not honored. |
| screen_shader | Screen shaders have no Windows meaning. |
| shadow | Shadows are not honored. |
| special_scale_factor | The master layout does not scale a special workspace. |
| vrr | Variable refresh rate is not honored. |
| xwayland | X11 compatibility has no Windows meaning. A window rule that matches on xwayland is not honored. |

## Kind and issue

Each keyword above is a `gap` or a `non-goal`. A non-goal has no Windows meaning at all. A gap is a feature Windows could support or Winland chose not to. The issue column links the open issue that tracks the keyword, or `-` when none does.

| kind | issue | keywords |
| --- | --- | --- |
| non-goal | - | allow_tearing, debug, opengl, quirks, render, screen_shader |
| gap | [#57](https://github.com/dante-sparras/winland/issues/57) | animation, animations, bezier |
| gap | - | binds:allow_pin_fullscreen, binds:movefocus_cycles_fullscreen |
| gap | - | bitdepth, cm, mirror, mode, scale, vrr |
| gap | [#58](https://github.com/dante-sparras/winland/issues/58) | blur, shadow |
| gap | - | center_ignores_reserved, special_scale_factor |
| non-goal | - | cursor:enable_hyprcursor, cursor:min_refresh_rate, cursor:no_break_fs_vrr, cursor:no_hardware_cursors, cursor:sync_gsettings_theme, cursor:use_cpu_buffer |
| gap | - | cursor:hide_on_tablet, cursor:hide_on_touch, cursor:hotspot_padding, cursor:invisible, cursor:warp_back_after_non_mouse_input, cursor:zoom_detached_camera, cursor:zoom_disable_aa, cursor:zoom_factor, cursor:zoom_rigid |
| gap | - | device |
| gap | - | dim_around |
| gap | - | dont_inhibit |
| non-goal | - | ecosystem, layerrule, layerrulev2, permission, xwayland |
| gap | - | gaps_workspaces |
| gap | - | gestures:close_max_timeout, gestures:workspace_swipe_touch, gestures:workspace_swipe_touch_invert |
| gap | - | gradient |
| gap | [#56](https://github.com/dante-sparras/winland/issues/56) | group |
| gap | - | input:emulate_discrete_scroll, input:force_no_accel, input:off_window_axis_events, input:resolve_binds_by_sym, input:rotation, input:scroll_button, input:scroll_button_lock, input:scroll_method, input:scroll_points |
| gap | - | input:kb_file, input:kb_layout, input:kb_model, input:kb_options, input:kb_rules, input:kb_variant |
| gap | - | input:tablet:absolute_region_position, input:tablet:active_area_position, input:tablet:active_area_size, input:tablet:left_handed, input:tablet:output, input:tablet:region_position, input:tablet:region_size, input:tablet:relative_input, input:tablet:transform |
| gap | - | input:tablettool:eraser_button_mode, input:tablettool:eraser_button_override, input:tablettool:pressure_range_max, input:tablettool:pressure_range_min |
| gap | - | input:touchdevice:enabled, input:touchdevice:output, input:touchdevice:transform |
| gap | - | input:touchpad:clickfinger_behavior, input:touchpad:disable_while_typing, input:touchpad:drag_3fg, input:touchpad:drag_lock, input:touchpad:flip_x, input:touchpad:flip_y, input:touchpad:middle_button_emulation, input:touchpad:natural_scroll, input:touchpad:scroll_factor, input:touchpad:tap-and-drag, input:touchpad:tap-to-click, input:touchpad:tap_button_map |
| gap | - | input:virtualkeyboard:release_pressed_on_close, input:virtualkeyboard:share_states |
| gap | - | locale |
| gap | - | misc:exit_window_retains_fullscreen, misc:on_focus_under_fullscreen |
| gap | - | modal_parent_blocking |
| gap | - | move_snap_cursor, move_snap_to_grid |
| gap | - | no_focus_fallback |

## Notes

`master:special_scale_factor` is the `special_scale_factor` gap: a special workspace uses the same master scale as a normal one. `master:center_ignores_reserved` is the `center_ignores_reserved` gap.

`gestures:scrolling:move_snap_to_grid` and `gestures:scrolling:move_snap_cursor` snap the `scrollMove` gesture. That action is not honored, so those options stay Gaps.

A `gesture` line is honored for the `workspace` action, described in [behavior.md](behavior.md#touchpad-swipe). Every other action (`dispatcher`, `move`, `resize`, `special`, `close`, `float`, `fullscreen`, `cursorZoom`, `scrollMove`) and every pinch direction (`pinch`, `pinchin`, `pinchout`) warns and does nothing when the swipe is made. The line is still kept, so it shadows a later gesture and `unset` removes it, as it would in the pinned release. Flag `p` (`gesturep`) warns and is ignored, for the same reason as bind flag `p`. Live Lua gestures wait on the Lua config (ADR 0013).

A touchpad swipe cannot be consumed. Windows reads the same fingers, so its own three- and four-finger gestures still run unless they are turned off (see [behavior.md](behavior.md#touchpad-swipe)). The swipe is not animated: the Workspace changes when the fingers lift, or at each full distance with `workspace_swipe_forever`. That is the `animations` Gap. `general:gaps_workspaces` stays a Gap for the same reason.

Bind flag `p` warns and is ignored. The bind itself still runs. Applications cannot inhibit binds, so there is nothing for that flag to bypass.

Per-device `device` options are not honored. Keyboard layout keys and the other input keys stay Gaps.

A window rule that matches on `xwayland` is a Gap: the rule is skipped and the config warns. Other effects and match fields are skipped with a warning until a later change honors them.

Mouse drag has these Gaps. Each is a gap with no open issue:

- A `bindm` press with no Client under the pointer is still consumed. The mouse hook decides from the binds alone, before it can look for a Client, so the press does not reach the desktop.
- The `mouse` dispatcher bound to a keyboard key warns and is skipped. A drag needs a mouse button to start and end it.
- A Client dragged onto another Monitor stays on its own Workspace, and a lifted tile drops back into that Workspace's Layout.
- In the monocle Layout a drag does not move or resize the Client.
- The pointer shape does not change while a Client is dragged or resized.
- The `keep_aspect_ratio` window rule is not honored, so `resizewindow 2` resizes exactly as no argument does.

## Commands

These commands are recognized and answer with an error. They are not honored.

| command | status | kind | issue | reason |
| --- | --- | --- | --- | --- |
| dismissnotify | error | gap | - | There is no notification surface. |
| kill | error | gap | - | Click to kill is not honored. |
| notify | error | gap | - | There is no notification surface. |
| rollinglog | error | gap | - | There is no rolling log. |
| setprop pin | error | gap | - | The pin dispatcher and the pin window rule honor pinning. This command does not. |

Every command above is a gap. No open issue tracks them.

`getprop` and `setprop` honor `float`. `getprop` reports `pin` as false. `setprop pin` is an error. Any other window property is not honored.

## Events

These events are not emitted. Each one waits on a feature this tree does not have yet.

`windowtitle` and `windowtitlev2` wait on a title change after a window is adopted. The title is read once, when the window appears.

`renameworkspace` waits on renaming a workspace. A workspace keeps the name it was created with.

Both events are gaps. No open issue tracks them.

## Plugins

`plugin` loads an in-process plugin assembly and is honored, so it is not a Gap. A plugin may register dispatchers, config keywords and event handlers. Registering a Layout is refused until a later change. Installing a plugin from a repository is not part of this.
