# Gaps

These keywords are not honored. The config parser warns with the line number, skips the line, and still loads the config. A keyword that is not listed here and is not one Winland reads is skipped with the same warning; it is not a Gap until it is recorded here.

Honored behavior is in [behavior.md](behavior.md), the additions Winland makes to Hyprland are in [extensions.md](extensions.md), and key and scan codes are in [keys.md](keys.md). The terms Gap and Non-goal are defined in [CONTEXT.md](../CONTEXT.md).

## Keywords

This table has one row per keyword. A test checks it against the keywords the config parser skips, so every Gap keyword is a row here.

| keyword | reason |
| --- | --- |
| acrylic | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| allow_tearing | Tearing is a compositor feature with no Windows meaning. |
| animation:fadeDim | Dimming is part of a window's alpha, so fadeSwitch animates it. |
| animation:fadeLayers | Layer shell surfaces have no Windows meaning. |
| animation:fadeLayersIn | Layer shell surfaces have no Windows meaning. |
| animation:fadeLayersOut | Layer shell surfaces have no Windows meaning. |
| animation:fadePopups | Wayland popups have no Windows meaning. |
| animation:fadePopupsIn | Wayland popups have no Windows meaning. |
| animation:fadePopupsOut | Wayland popups have no Windows meaning. |
| animation:layers | Layer shell surfaces have no Windows meaning. |
| animation:layersIn | Layer shell surfaces have no Windows meaning. |
| animation:layersOut | Layer shell surfaces have no Windows meaning. |
| animation:monitorAdded | A monitor that is added does not animate. |
| aurora | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| binds:disable_keybind_grabbing | Applications cannot ask Windows to turn binds off, so there is nothing to refuse. |
| bitdepth | Changing a monitor's bit depth is not honored. |
| cursor:enable_hyprcursor | The cursor theme format has no Windows meaning. |
| cursor:min_refresh_rate | Cursor refresh rate has no Windows meaning. |
| cursor:no_break_fs_vrr | Pausing frames while the cursor moves has no Windows meaning. |
| cursor:no_hardware_cursors | Hardware cursors have no Windows meaning. |
| cursor:sync_gsettings_theme | Syncing the cursor theme with the desktop has no Windows meaning. |
| cursor:use_cpu_buffer | Hardware cursor buffers have no Windows meaning. |
| cursor:zoom_disable_aa | The system magnifier offers plain or edge-smoothed scaling, not unfiltered pixels. |
| debug | Compositor debug options have no Windows meaning. |
| decoration:blur:brightness | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:contrast | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:ignore_opacity | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:input_methods | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:input_methods_ignorealpha | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:new_optimizations | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:noise | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:passes | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:popups | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:popups_ignorealpha | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:size | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:special | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:variant | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:vibrancy | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:vibrancy_darkness | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| decoration:blur:xray | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| device | Per-device input is not honored. Honored input settings apply to the whole system. |
| dont_inhibit | Applications cannot inhibit binds, so this flag has nothing to bypass. |
| drops | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| ecosystem | Ecosystem tools have no Windows meaning. |
| experimental | Experimental Wayland protocol options have no Windows meaning. |
| fluid_jar | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| glass | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| group:groupbar:blur | Groupbar blur is not honored. |
| haze | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| heat_shimmer | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| input-capture | The input capture portal has no Windows meaning. |
| input:emulate_discrete_scroll | High resolution scroll emulation is not honored. |
| input:kb_file | XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant. |
| input:kb_model | XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant. |
| input:kb_rules | XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant. |
| input:off_window_axis_events | Scroll events outside a window are not honored. |
| input:tablet:absolute_region_position | Tablets are not honored. |
| input:tablet:active_area_position | Tablets are not honored. |
| input:tablet:active_area_size | Tablets are not honored. |
| input:tablet:enabled | Tablets are not honored. |
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
| input:touchdevice:enabled | Windows turns a touch screen off only by disabling its device, which needs administrator rights. |
| input:touchdevice:output | Windows maps a touch screen to a display in its Tablet PC settings, which Winland cannot set. The touch screen swipe reads the display Windows maps it to. |
| input:touchdevice:transform | Windows turns touch input with its display's orientation and has no setting to turn it apart from that. |
| input:touchpad:middle_button_emulation | A button press reaches the mouse hook without its device, so a touchpad's buttons cannot be told from a mouse's, and Windows has no such touchpad setting. |
| input:virtualkeyboard:release_pressed_on_close | Virtual keyboards are not honored. |
| input:virtualkeyboard:share_states | Virtual keyboards are not honored. |
| layerrule | Layer shell rules have no Windows meaning. |
| layerrulev2 | Layer shell rules have no Windows meaning. |
| locale | Locale override is not honored. |
| misc:allow_session_lock_restore | The lock screen belongs to Windows. |
| misc:always_follow_on_dnd | Windows tells no other program that a drag and drop is under way, so focus cannot follow the pointer only then. |
| misc:anr_missed_pings | Windows marks a hung window as not responding itself. |
| misc:background_color | The desktop background belongs to Windows. |
| misc:bell_sound | The system bell sound is set in Windows. |
| misc:col.splash | There is no splash text. |
| misc:disable_scale_notification | Windows sets each monitor's scale, so there is no scale notification. |
| misc:disable_splash_rendering | There is no splash text. |
| misc:disable_watchdog_warning | There is no start watchdog, so there is no warning to turn off. |
| misc:disable_xdg_env_checks | XDG environment variables have no Windows meaning. |
| misc:enable_anr_dialog | Windows marks a hung window as not responding itself. |
| misc:force_default_wallpaper | There is no wallpaper. The desktop background belongs to Windows. |
| misc:layers_hog_keyboard_focus | Layer shell surfaces have no Windows meaning. |
| misc:lockdead_screen_delay | The lock screen belongs to Windows. |
| misc:middle_click_paste | The primary selection has no Windows meaning. |
| misc:name_vk_after_proc | Virtual keyboards are not honored. |
| misc:render_unfocused_fps | Each application renders itself, so its frame rate has no Windows meaning. |
| misc:screencopy_force_8b | Screen copy buffers have no Windows meaning. |
| misc:session_lock_blur | The lock screen belongs to Windows. |
| misc:session_lock_xray | The lock screen belongs to Windows. |
| misc:splash_font_family | There is no splash text. |
| motion_blur | Motion blur is not honored. |
| opengl | GPU rendering options have no Windows meaning. |
| permission | Permission rules have no Windows meaning. |
| quirks | Display buffer quirks have no Windows meaning. |
| render | GPU rendering options have no Windows meaning. |
| ripple | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| screen_shader | Screen shaders have no Windows meaning. |
| vrr | Windows has no switch for a display's adaptive sync, since each game turns VRR on for itself. The Variable refresh rate graphics setting only lets DirectX 11 fullscreen games without VRR of their own use it, and Dynamic refresh rate has no documented interface. |
| water | Only decoration:blur:enabled is honored. The DWM backdrop cannot be tuned. |
| wobble | DWM cannot deform a window, so wobble is not honored. |
| xwayland | X11 compatibility has no Windows meaning. A window rule that matches on xwayland is not honored. |

## Kind and issue

Each keyword above is a `gap` or a `non-goal`. A non-goal has no Windows meaning at all. A gap is a feature Windows could support or Winland chose not to. [Possible](#possible) names every uncovered keyword and behavior Windows can support. The issue column links the open issue that tracks the keyword, or `-` when none does.

| kind | issue | keywords |
| --- | --- | --- |
| gap | - | acrylic, aurora, decoration:blur:variant, drops, fluid_jar, glass, haze, heat_shimmer, ripple, water |
| non-goal | - | allow_tearing, debug, opengl, quirks, render, screen_shader |
| gap | - | animation:fadeDim, animation:monitorAdded |
| non-goal | - | animation:fadeLayers, animation:fadeLayersIn, animation:fadeLayersOut, animation:fadePopups, animation:fadePopupsIn, animation:fadePopupsOut, animation:layers, animation:layersIn, animation:layersOut |
| non-goal | - | binds:disable_keybind_grabbing |
| gap | - | bitdepth, vrr |
| non-goal | - | cursor:enable_hyprcursor, cursor:min_refresh_rate, cursor:no_break_fs_vrr, cursor:no_hardware_cursors, cursor:sync_gsettings_theme, cursor:use_cpu_buffer |
| gap | - | cursor:zoom_disable_aa |
| gap | - | decoration:blur:brightness, decoration:blur:contrast, decoration:blur:ignore_opacity, decoration:blur:input_methods, decoration:blur:input_methods_ignorealpha, decoration:blur:new_optimizations, decoration:blur:noise, decoration:blur:passes, decoration:blur:popups, decoration:blur:popups_ignorealpha, decoration:blur:size, decoration:blur:special, decoration:blur:vibrancy, decoration:blur:vibrancy_darkness, decoration:blur:xray |
| gap | - | group:groupbar:blur, wobble |
| gap | - | device |
| gap | - | dont_inhibit |
| non-goal | - | ecosystem, layerrule, layerrulev2, permission, xwayland |
| non-goal | - | experimental, input-capture |
| gap | - | motion_blur |
| gap | - | input:emulate_discrete_scroll, input:off_window_axis_events |
| non-goal | - | input:kb_file, input:kb_model, input:kb_rules |
| gap | - | input:tablet:absolute_region_position, input:tablet:active_area_position, input:tablet:active_area_size, input:tablet:enabled, input:tablet:left_handed, input:tablet:output, input:tablet:region_position, input:tablet:region_size, input:tablet:relative_input, input:tablet:transform |
| gap | - | input:tablettool:eraser_button_mode, input:tablettool:eraser_button_override, input:tablettool:pressure_range_max, input:tablettool:pressure_range_min |
| gap | - | input:touchdevice:enabled, input:touchdevice:output, input:touchdevice:transform |
| gap | - | input:touchpad:middle_button_emulation |
| gap | - | input:virtualkeyboard:release_pressed_on_close, input:virtualkeyboard:share_states, misc:name_vk_after_proc |
| gap | - | locale |
| non-goal | - | misc:allow_session_lock_restore, misc:anr_missed_pings, misc:background_color, misc:bell_sound, misc:col.splash, misc:disable_scale_notification, misc:disable_splash_rendering, misc:disable_watchdog_warning, misc:disable_xdg_env_checks, misc:enable_anr_dialog, misc:force_default_wallpaper, misc:layers_hog_keyboard_focus, misc:lockdead_screen_delay, misc:middle_click_paste, misc:render_unfocused_fps, misc:screencopy_force_8b, misc:session_lock_blur, misc:session_lock_xray, misc:splash_font_family |
| gap | - | misc:always_follow_on_dnd |

## Possible

These are the uncovered keywords and behaviors Windows can honor. A gap keyword in the tables above is in this list when Windows has the thing it names. The seven logging and error-bar options are the part of the `debug` non-goal that can be honored, and `misc:name_vk_after_proc` is grouped with the virtual-keyboard gaps.

Input Windows already has: `device`, so an honored input setting can name one device; `input:tablet:enabled`, `input:tablet:left_handed`, `input:tablet:output`, `input:tablet:transform`, `input:tablet:region_position`, `input:tablet:region_size`, `input:tablet:absolute_region_position`, `input:tablet:relative_input`, `input:tablet:active_area_position` and `input:tablet:active_area_size`; `input:tablettool:eraser_button_mode`, `input:tablettool:eraser_button_override`, `input:tablettool:pressure_range_min` and `input:tablettool:pressure_range_max`; `input:virtualkeyboard:share_states`, `input:virtualkeyboard:release_pressed_on_close` and `misc:name_vk_after_proc`; `input:emulate_discrete_scroll` and `input:off_window_axis_events`.

Monitors and the process locale: `bitdepth` and `locale` (`general:locale`).

Drawing on windows Winland already owns: `group:groupbar:blur` on the bar Winland draws; `motion_blur`, which is `decoration:motion_blur:enabled` and `decoration:motion_blur:samples`; and the animation nodes `animation:fadeDim` and `animation:monitorAdded`.

Logging and the error bar: `debug:disable_logs`, `debug:disable_time`, `debug:enable_stdout_logs`, `debug:colored_stdout_logs`, `debug:suppress_errors`, `debug:error_limit` and `debug:error_position`.

Events and calls that have a Windows source: `window.open_early`, `config.props_refreshed` and `input.keyboard.key`; `hl.notification`, `hl.load` and `hl.is_key_down`; the methods `hl.window_rule` and `hl.workspace_rule` return; a spring `hl.curve`; `pseudo` on a custom layout; `workspace.active` for each monitor; `monitor.layout_changed` when a mode changes or a reload changes the rules; a real focus reason on `window.active`; a bind's `device` and `auto_consuming`; a description that holds a comma; `HL.Group:add` leaving the focus where it was; `window.clear_tags`; `force_idle`; `focus` with `last = true`; and a window selector on `cursor.move_to_corner`, the group toggles, `window.fullscreen` and `window.center`.

Behavior the Notes describe, which Windows can do: the modifiers `CAPS`, `MOD2`, `MOD3` and `MOD5`; animating a Client through fullscreen mode 2; animating a window while it is dragged; a workspace rule's `animation:` style; an `animation` window rule; tracking a window's owner; the mouse-drag gaps; dragging a tab out of the bar; reading `focusurgentorlast` from the newest end of the history; swallowing the way the pinned release does; `general:resize_on_border` on the monitor under the pointer; `movetoworkspace` of a pinned Client onto a special workspace still showing that workspace; `alterzorder` and `bringactivetotop` refocusing the window under the pointer; and the monitor fields `supports_wide_color`, `supports_hdr`, `sdr_min_luminance`, `sdr_max_luminance`, `min_luminance`, `max_luminance` and `max_avg_luminance`.

Commands and the plugin manager: `descriptions` carrying each option's description; `clients` and `activewindow` in the text form; `changeworkspaceid` taking a number up to 4294967295; the window properties `no_vrr`, `no_follow_mouse`, `scroll_mouse` and `scroll_touchpad`; plugin `enable`, `disable` and `reload`; `remove` taking `author/name`; a manifest's shell `build` steps and `output`; and the `--notify`, `--verbose` and `--no-shallow` flags, and the custom source URL flag.

## Notes

An `animation:NAME` row is a node of the animation tree. An `animation` line that names it warns with that reason and is skipped. The other nodes are honored; see [behavior.md](behavior.md#animations). These parts of the honored nodes are Gaps:

- A window that closes by itself is gone before it can be drawn again, so `windowsOut` and `fadeOut` play only when Winland closes the window, with `killactive` or `closewindow`.
- A Client entering or leaving fullscreen mode 2 moves at once.
- Dragging a window with the pointer moves it at once. The windows that make room for it animate when it is dropped.
- A workspace rule's `animation:` style is not honored, and warns.
- An `animation` window rule is not honored.
- Hyprland scales a growing window's last frame into each animated rectangle. Winland shows it unscaled from its top left and cut to the rectangle, the way a shrinking window shows, and the see-through areas of a window with a system backdrop look flatter while it grows.

A row named by one part of a keyword covers every keyword with that part. `decoration:wobble:enabled` is the `wobble` gap, and `decoration:blur:glass:size` is the `glass` gap.

Two `misc` options turn off the reference compositor's logo and its check for its GUI utilities. Both are non-goals: Winland draws no logo and has no GUI utilities. Their names contain the name that [ADR 0002](adr/0002-hyprland-compatibility-without-its-name.md) keeps out of the code, so neither has a row above or an entry in the gap list in code. Each warns like any keyword Winland does not read.

A `gesture` line is honored for the `workspace`, `special`, `dispatcher`, `close`, `float`, `fullscreen`, `move`, `resize`, `cursorZoom` and `scrollMove` actions, and Winland's `luafunction`, described in [behavior.md](behavior.md#touchpad-swipe). `cursorZoom` zooms from 1 to 10, as `cursor:zoom_factor` does here, where the pinned release goes to 100, and a `live` pinch zooms around the point the camera follows rather than pinning the point under the cursor. The line is still kept, so it shadows a later gesture and `unset` removes it, as it would in the pinned release. Flag `p` (`gesturep`) warns and is ignored, for the same reason as bind flag `p`. A gesture sent with `keyword`, as `hl.gesture` called while Winland runs sends it, is added after the configured ones without the shadow warning, and `unset` there answers `Can't remove a non-existent gesture.` A Lua gesture's callback sees how far a pinch's fingers moved, but not its `scale` or `rotation`.

A touchpad swipe cannot be consumed. Windows reads the same fingers, so its own three- and four-finger gestures still run unless they are turned off (see [behavior.md](behavior.md#touchpad-swipe)). A window the swipe moves is moved itself, not drawn offset as the pinned release's compositor draws it, so a slow application can lag behind the fingers.

Bind flag `p` warns and is ignored. The bind itself still runs. Applications cannot inhibit binds, so there is nothing for that flag to bypass.

The modifiers `CAPS`, `MOD2`, `MOD3` and `MOD5` are not honored. A modifier list that names one warns `Modifier '<name>' is not honored.` and the bind, gesture or `sendshortcut` is skipped. Winland tracks Shift, Ctrl, Alt and the Windows key as held modifiers, and has no held state for the lock and level modifiers.

Blur is DWM's acrylic backdrop, so it shows only where DWM draws that backdrop: the title bar and any area a window leaves for it. What a window paints itself is not blurred, and a translucent Client does not blur what is behind its content. The blur `size`, `passes` and color options have no DWM equivalent. `motion_blur` is not drawn. A corner radius DWM does not draw is cut as a window region, and that edge is not antialiased. The drawn shadow surrounds the window and its border but not a Group's bar, and a `sharp` shadow is not drawn under the window, so a translucent Client does not show it through. The `no_blur`, `rounding` and `rounding_power` window rule effects are not honored. The backdrop, shadow and DWM corner preference are not undone after a crash. A corner region Winland cut is removed on the next start.

Per-device `device` options are not honored. The other input keys stay Gaps.


`pass`, `sendshortcut` and `sendkeystate` to a window that is not focused bring that window to the foreground, send the key, and give the foreground back 30 ms later. Windows gives keys only to the foreground window. Each change of focus is reported. A mouse button goes to the window under the pointer, not to the named window.

`pass` from the pipe has no key to send and answers `pass: no key to pass`. The reference passes the last key pressed, and Winland sees only the keys of its binds.

`focusurgentorlast` with no urgent Client focuses the Client focused before the current one. The reference reads its focus history from the oldest end there.

An application that asks for attention without flashing or taking the foreground is not urgent. `misc:focus_on_activate` set to true does not report the application as urgent before it takes focus.

`inhibitingIdle` reports only the `idle_inhibit` rule. An application that keeps the system awake by itself does so without Winland, and is not reported.

Swallowing reads the process tree up to 16 levels. The reference reads 25. A parent shown in a Group hands the child its place in the Group, and a Group that denies new windows, or that the child may not join, keeps its parent. The pinned release has no Group slot: it takes the parent's whole Group out of the Layout while the parent is swallowed, a child that joined that Group by `group:auto_group` included, and puts the Group back when the child closes, so the child and the other members are left unplaced until then. A parent that is a hidden Group member, or whose Group is on another Workspace than the child, is not swallowed. `toggleswallow` on a child that holds its parent's place in a Group shows the parent as its own tile, where the pinned release puts the parent's Group back in the Layout.

A new floating window that has an owner opens centered in the floating work area, not over its owner. Owners are not tracked yet. A window that asks for a position does not keep it: every new floating Client opens centered unless `move` places it.

A `move` or `size` expression honors arithmetic, parentheses, the constants `_pi` and `_e`, and one-argument functions. Multi-argument functions such as `min` and `max`, comparison, logical and conditional operators, and the other built-in names are not honored: the rule warns and is skipped. A comma ends the effect in a window rule line, so a multi-argument function could not be written there anyway.

In a `move` expression, `window_w` and `window_h` are the size after a `size` rule. The pinned release reads the size the window asked for there, which a `size` rule does not change. Winland uses the size the window will have, so `move monitor_w-window_w 0` lines the window up with the right edge.

A scrolling resize with `scrolling:direction` up or down measures each delta along its own axis. The pinned release divides the column delta by the work area's width and the row delta by its height in every direction.

A pinned Client on a Monitor that is unplugged or disabled stays shown on the Monitor that takes its Workspaces. The pinned release moves it onto the Workspace the leaving Monitor shows next, which is a new, empty Workspace once the others have moved, and leaves that Workspace on the Monitor that went away, so the Client is not shown anywhere. Winland does not hide a window where nothing can reach it.

When a Monitor goes away or comes back and its Workspaces hide the focused Client, Winland focuses the Client the focused Monitor shows: the one focused last on that Workspace, or else its last shown Client. The pinned release moves only the Monitor focus and leaves the keyboard focus on the hidden window until the pointer or a dispatcher moves it. On Windows that would keep the foreground on a window out of sight.

`general:resize_on_border` looks for the grab ring on the focused Monitor's shown Workspace. The pinned release looks on the Monitor under the pointer. While a maximized or fullscreen Client covers a Workspace, no Client there has a ring, where the pinned release keeps the ring of a floating window allowed over the covering one, such as a floating window opened after it.

`movetoworkspace` of a pinned Client to a special workspace changes nothing. The pinned release leaves the Client where it is too, but still shows that special workspace, empty.

`alterzorder` and `bringactivetotop` do not refocus the window under the pointer. With `input:follow_mouse`, focus follows the pointer again on its next move.

A window rule that matches on `xwayland` is a Gap: the rule is skipped and the config warns. Other effects and match fields are skipped with a warning until a later change honors them.

Mouse drag has these Gaps. Each is a gap with no open issue:

- A `bindm` press with no Client under the pointer is still consumed. The mouse hook decides from the binds alone, before it can look for a Client, so the press does not reach the desktop.
- The `mouse` dispatcher bound to a keyboard key warns and is skipped. A drag needs a mouse button to start and end it.
- A Client dragged onto another Monitor stays on its own Workspace, and a lifted tile drops back into that Workspace's Layout.
- In the monocle Layout a drag does not move or resize the Client.
- The pointer shape does not change while a Client is dragged or resized.

Dragging a tab out of a tab bar is a Gap: a press on a tab focuses that member, and a drag from it moves nothing. `group:groupbar:blur` is the `blur` gap. Titles are drawn without antialiasing, because antialiased text would blend with the transparent strip. The wheel over the bar focuses the next member instead of only showing it. The bar is a separate window. It is put just above the shown member in the z-order each time the core reports a change, not each time another window is raised.

## Lua config

A `winland.lua` keeps running while Winland runs (ADR 0013), but these are gaps: `hl.notification`, and the methods of the rule objects `hl.window_rule`, `hl.layer_rule` and `hl.workspace_rule` return, which warn once. The `hl` calls not built yet warn once and do nothing: `hl.load` and `hl.is_key_down`. A custom layout from `hl.layout.register` runs on the Lua thread after the change it answers, where the pinned release runs it inside that change (ADR 0013), so for a moment after a window opens every window takes a cell of an even grid, and after one closes or moves the others keep their boxes, until its boxes arrive. For the same reason a `layoutmsg` its `layout_msg` rejects, with false or a message, replies `ok` and the rejection goes to the log, as an error in its callbacks does, where the pinned release replies with it and shows the first error in the error bar. `pseudo` does not change a custom layout's tiles. `require` looks a module name up in the config's folder only, where the pinned release goes on to Lua's own search path, because Winland's Lua reads no file the config's reader does not (ADR 0013).
`hl.on` takes these events and never runs their handlers, since Winland has no such event: `window.open_early`, `window.bell` (a window that asks for attention on Windows is `window.urgent`), `layer.opened` and `layer.closed` (Windows has no layer surfaces), `config.props_refreshed`, `screenshare.state`, `input.keyboard.key`, and the events a plugin adds. `monitor.layout_changed` runs only when a monitor is added or removed, not when a mode changes or a reload changes the rules. `workspace.active` runs for the focused monitor's workspace, not for each monitor's. The focus reason `window.active` gets is always 0, unknown. The pinned release runs a handler inside the change that raised its event and hands it objects of that moment. Winland runs it after the change, and the objects read Winland as it is then, so the window `window.close` gets reads nil. For the same reason `config.unload` runs once a reload has read the new config, where the pinned release runs it before.

The objects the `hl.get_*` queries return differ where Winland's replies have less: a window's `xwayland` is false and its `layout` has only `name`; a group's `locked` and `denied` are nil and its `current` is the member that is not hidden; each of a monitor's `available_modes` is `preferred = false`, and its `hardware_details` are `backend = "windows"` and false for the rest. `get_devices` lists the keyboards and mice Winland reports, whose fields are those of its `devices` reply. A keybind's `auto_consuming`, `device_inclusive` and `allow_input_capture` are false and its `devices` empty, its `arg` is the line dispatcher's argument, and disabling or removing it unbinds every bind of its keys. The urgent window, the last window, `focus_history_id` and the last workspace start empty when the config is loaded or reloaded. A monitor selector takes its id, name or `desc:`, but not the relative forms, such as `+1` or a direction. Within one call a reply is read once, so a change Winland makes by itself in that time, such as a window opening, shows only after a dispatch or in the next call. A second `hl.window_rule` or `hl.layer_rule` with the name of an earlier one warns and changes nothing, where the pinned release adds to the earlier rule. A value with a comma in a rule, a monitor or a workspace rule warns and is left out, since the line form splits at commas. Spring curves are a gap: `hl.curve` with `type = "spring"` warns, and an `hl.animation` that names a spring warns and makes no line. The monitor HDR range fields (`supports_wide_color`, `supports_hdr`, `sdr_min_luminance`, `sdr_max_luminance`, `min_luminance`, `max_luminance`, `max_avg_luminance`) warn, as the other monitor color management does. These `hl.dsp` dispatchers have no line dispatcher and warn: `window.signal`, `window.clear_tags`, `global`, `force_idle`, `release_input_capture` and `focus({ last = true })`, and a `window` selector on `cursor.move_to_corner`, the `group` toggles, `window.fullscreen` and `window.center`, which act only on the focused window. A bind's `device` option makes no bind, and `auto_consuming` warns. A description with a comma is dropped, since the line form cannot hold it.

`HL.Group:add` moves the focus to the group's shown member, as `changegroupactive` does, where the pinned release leaves it on the window it was on even when that window is now hidden in the group. A group object names the group of the window it was read from, so when that window leaves the group by another way, the object follows the window, not the group.

## Commands

Every command the pinned release's control socket answers is recognized. The ones below differ.

`descriptions` lists only the options Winland reads, and their `description` is empty: the pinned release's descriptions are not carried.

`clients` and `activewindow` reply in JSON with or without `j/`, where the pinned release replies in text without it. With no window they reply its text, `no open windows` and `Invalid`.

`getprop` reads and `setprop` sets every window property the pinned release has, described in [behavior.md](behavior.md#window-properties). These are kept and reported but change nothing: `animation`, `allows_input`, `decorate`, `nearest_neighbor`, `no_anim`, `no_blur`, `no_dim`, `no_wobble`, `no_shortcuts_inhibit`, `force_rgbx`, `sync_fullscreen`, `immediate`, `xray`, `render_unfocused`, `no_follow_mouse`, `no_screen_share`, `no_vrr`, `no_auto_hdr`, `scroll_mouse` and `scroll_touchpad`. Each waits on the feature it would change, most of them Gaps above. A border color property is drawn with all its colors and its angle.

`changeworkspaceid` refuses a number above 2147483647. The pinned release takes numbers up to 4294967295, but a Workspace number here is a signed 32-bit integer. It is a gap.

## Plugins

`plugin` loads an in-process plugin assembly and is honored, so it is not a Gap. A plugin may register dispatchers, config keywords and event handlers. Registering a Layout is refused until a later change.

`winlandctl plugin install`, `update`, `remove` and `list --installed` are the plugin manager's `add`, `update`, `remove` and `list`. See [plugins.md](plugins.md). These parts of the plugin manager are Gaps:

- `enable` and `disable` are not honored. An installed plugin loads when a `plugin` line names it, or through `winlandctl plugin load`.
- `reload` is not honored. `winlandctl reload` reads the `plugin` lines again.
- `purge-cache` is not honored. There are no headers to cache, and `remove` deletes a repository's staging folder.
- `remove` takes a repository URL or a plugin name, not `author/name`.
- A manifest's shell `build` steps and `output` are not honored. Winland runs `dotnet publish` on the plugin's `project`.
- Commit pins and the per-plugin minimum compositor version are not honored. The exact plugin API version replaces them, and header and ABI checks with it.
- `--notify`, `--verbose`, `--no-shallow`, `--no-nix` and the custom source URL flag are not honored.
