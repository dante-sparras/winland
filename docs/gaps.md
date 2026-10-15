# Gaps

These keywords are not honored. The config parser warns with the line number, skips the line, and still loads the config. A keyword that is not listed here and is not one Winland reads is skipped with the same warning; it is not a Gap until it is recorded here.

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
| gesture | Gestures are not honored. |
| gestures | Gestures are not honored. |
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
| precise_mouse_move | Dragging a window onto a split is not honored. |
| quirks | Display buffer quirks have no Windows meaning. |
| render | GPU rendering options have no Windows meaning. |
| scale | Changing a monitor's scale is not honored. |
| screen_shader | Screen shaders have no Windows meaning. |
| shadow | Shadows are not honored. |
| special_scale_factor | The master layout does not scale a special workspace. |
| vrr | Variable refresh rate is not honored. |
| xwayland | X11 compatibility has no Windows meaning. A window rule that matches on xwayland is not honored. |

`general:layout` is honored for `dwindle`, `master`, `monocle`, and `scrolling`. Any other value warns with its line number. Dwindle still runs when the named layout is not available.

`master:special_scale_factor` is the `special_scale_factor` gap: a special workspace uses the same master scale as a normal one. `master:center_ignores_reserved` is the `center_ignores_reserved` gap.

`gestures:scrolling:move_snap_to_grid` and `gestures:scrolling:move_snap_cursor` snap a trackpad gesture. Gestures are not honored, so those options stay Gaps.

Bind flag `p` warns and is ignored. The bind itself still runs. Applications cannot inhibit binds, so there is nothing for that flag to bypass.

`decoration:rounding` selects square corners when the value is 0 and rounded corners otherwise. The exact radius is not applied.

A border color is one color, written as `rgba()`, `rgb()`, or `0xAARRGGBB`. A gradient keeps the first color. Gradient borders are not honored.

`decoration:active_opacity`, `decoration:inactive_opacity` and `decoration:fullscreen_opacity` are fractions from 0 to 1 and default to 1. A value outside that range is clamped. A Client in fullscreen mode 2 uses `fullscreen_opacity`. A maximized Client uses the active or inactive fraction, and so does every other Client. |

An `opacity` window rule supplies up to three fractions: active, inactive and fullscreen. One fraction is used for all three. Two fractions set active and inactive and leave fullscreen at 1, which then multiplies the configured fullscreen fraction. Each fraction multiplies the configured fraction unless the next token is `override`, which uses the rule's fraction alone. The last matching `opacity` rule wins. `opaque` forces the fraction to 1, and a trailing `0` turns that off. The last matching `opaque` rule wins.

`decoration:dim_inactive` defaults to false and `decoration:dim_strength` defaults to 0.5. When dimming is on, an inactive Client's fraction is multiplied by `1 - dim_strength`. Inactive windows are dimmed by tinting them toward black. That tint cannot be drawn on a window here, so dimming lowers the window's alpha instead. `decoration:dim_modal` defaults to true. A Client shadowed by a modal moves that dim halfway toward 1 before the alpha is taken. Modal parents are not tracked yet, so the extra dim does not apply. `decoration:dim_special` defaults to 0.2 and multiplies the alpha of Clients on a Monitor whose special workspace is open, except the Clients on that special workspace. `decoration:dim_around` is a Gap. Blur and shadow stay Gaps.

An alpha below 255 adds `WS_EX_LAYERED` and sets the window's alpha. The window's previous extended style is restored when it stops being a Client, on a clean exit, and after a crash for every window this program layered. A window that was already layered is left alone. A window that refuses the layered style is skipped.

A rule block on `exec`, `exec-once`, `execr`, `execr-once`, `exec-shutdown`, or the `exec` and `execr` dispatchers applies to the first window mapped by that process or a child of it. `float`, `tile`, `workspace`, `ignore`, `opacity`, `opaque`, and `pin` are honored. Any other effect, including `move`, `size`, and `center`, warns and is skipped. The command still launches. An unclosed rule block warns and does not launch. A known effect with a bad argument warns and does not launch.

## Border resize and floating snap

`general:resize_on_border` defaults to false. When it is on, a press in the grab ring around a Client resizes that Client. The ring is `general:border_size` plus `general:extend_border_grab_area` (0 to 100, default 15) outside the Client. A press inside the Client is left to the Client. A rounded corner is not a grab: the corner radius is not applied, so that part of the window stays a normal click.

`general:hover_icon_on_border` defaults to true and is used only while border resize is on. The outcome names the cursor for the edge under the pointer: `top_side`, `bottom_side`, `left_side`, `right_side`, `top_left_corner`, `top_right_corner`, `bottom_left_corner`, or `bottom_right_corner`. The corner zone is the border plus 10 pixels. The system pointer is not changed.

`general:resize_corner` defaults to 0. For a floating Client, 1, 2, 3, and 4 force the grab to the top left, top right, bottom right, and bottom left. 0 uses the corner nearest the pointer. Names `disable`, `top_left`, `top_right`, `bottom_right`, and `bottom_left` are the same values.

`general:float_gaps` is the gap between a floating Client and the monitor edges, as one to four integers, default 0. A negative side uses `general:gaps_out` instead. Floating Clients are kept inside that inset.

`general:snap:enabled` defaults to false. While a floating Client is moved or resized, `general:snap:window_gap` (default 10) snaps it to another Client and `general:snap:monitor_gap` (default 10) snaps it to the monitor. Both are 0 to 100, and 0 turns that snap off. `general:snap:border_overlap` defaults to false. When it is on, the snap meets the monitor so the border overlaps the edge. `general:snap:respect_gaps` defaults to false. When it is on, window snaps leave `general:gaps_in` between Clients and monitor snaps leave `general:gaps_out` at the edges.

## Cursor and input

These cursor keys are honored: `warp_on_change_workspace`, `warp_on_monitor_change`, `warp_on_toggle_special`, `no_warps`, `persistent_warps`, `inactive_timeout`, `hide_on_key_press`, and `default_monitor`. Every other `cursor` key stays a Gap.

`warp_on_change_workspace` and `warp_on_toggle_special` default to 0. 1 warps to the middle of the last focused window, or to the cursor's last point in that window when `persistent_warps` is on. 2 does the same and still warps when `no_warps` is on. `warp_on_monitor_change` defaults to -1, which follows `warp_on_change_workspace`. The pinned release does not define that key; it is accepted with the same modes, plus -1. `warp_on_toggle_special` warps when `togglespecialworkspace` shows or hides a special workspace.

`inactive_timeout` is seconds from 0 to 20, default 0. Above 0 the cursor hides once it has been still for longer than that. `hide_on_key_press` hides it on a key press until it moves. `default_monitor` warps once to the middle of the named monitor. An empty value does not warp, and `no_warps` does not block that warp.

These input keys are honored: `repeat_rate`, `repeat_delay`, `numlock_by_default`, `left_handed`, `natural_scroll`, `accel_profile`, `sensitivity`, and `scroll_factor`. A key left unset does not change the system. The value it replaced is saved and put back on a clean exit, and again on the next start if the process crashed.

`repeat_delay` is milliseconds, mapped to the four system delays 250, 500, 750 and 1000. A tie takes the longer delay. `repeat_rate` is repeats per second, mapped onto system speed 0 through 31, where 0 is about 2.5 repeats per second and 31 is about 30. `sensitivity` from -1 to 1 maps onto pointer speed 1 through 20, and 0 maps to 10. `scroll_factor` from 0 to 2 maps onto wheel lines as the factor times 3. `accel_profile` `flat` turns pointer acceleration off. `adaptive` turns it on with thresholds 6 and 10. `custom` warns and is not applied. `natural_scroll` reverses wheel direction as the event is handled. It is not a system setting. `left_handed` swaps the mouse buttons. `numlock_by_default` toggles Num Lock only when it is not already in that state.

`input:follow_mouse` defaults to 1. 0 leaves keyboard focus where it is when the pointer moves. 1 moves it to the Client under the pointer once the pointer has traveled farther than `input:follow_mouse_threshold` logical pixels. That distance accumulates while the pointer keeps moving and resets after the pointer has been still for half a second. 2 and 3 do not move keyboard focus when the pointer moves. 3 also keeps the focused Client when a click would take it. The names `disabled`, `follow`, `detached`, and `separate` are 0, 1, 2, and 3.

`input:follow_mouse_shrink` insets every Client except the focused one by that many logical pixels. The pointer in the inset does not move keyboard focus. The window under the pointer still receives pointer input.

`input:mouse_refocus` defaults to true. When it is false, mode 1 does not move keyboard focus back to the Client already under the pointer until the pointer crosses onto another Client.

`input:float_switch_override_focus` defaults to 1. In modes 0 and 2, moving between a tiled Client and a floating Client moves keyboard focus. 2 also does so between two floating Clients. 0 does not. Mode 3 does not use it.

`input:focus_on_close` defaults to 0. 0 focuses the nearest remaining tiled Client, or the most recently focused Client when the one that closed was floating. 1 focuses the Client under the pointer. 2 focuses the most recently focused Client. The names `next`, `cursor`, and `mru` are 0, 1, and 2. A master layout with `master:focus_master_on_close` still focuses the master when the closed Client was tiled and the mode is 0.

`misc:mouse_move_focuses_monitor` defaults to true. Moving the pointer onto another Monitor focuses that Monitor. False leaves it until a Client there takes keyboard focus.

Modes 2 and 3 cannot choose which window receives pointer input. The window under the pointer still receives it. Those modes only decide keyboard focus.

Per-device `device` options are not honored. Keyboard layout keys and the other input keys stay Gaps.

## Winland extensions

`[elevated]` at the start of an `exec`, `exec-once`, `execr`, `execr-once`, or `exec-shutdown` command, or of an `exec` or `execr` dispatcher argument, launches that program with administrator rights. The same word inside a rule block does too, alone or with other rules. Without it the launch is de-elevated. A command received on the pipe never launches elevated, even when it starts with `[elevated]` or names it in the block.

`exe` is a `windowrulev2` match on the executable file name, including its extension. It uses the same regular expressions as `class` and `title`. `initialClass` and `initial_class` match the class, and `initialTitle` and `initial_title` match the title, because a rule is applied once when the window appears.

`aumid` is a `windowrulev2` match on a packaged app's AppUserModelID. It uses the same regular expressions as `class`, `title` and `exe`. For a Store app hosted in a frame, `class` and `exe` are the hosted app's window class and executable file name, and `aumid` is that app's AppUserModelID.

`ignore` leaves the window unmanaged. `float`, `tile` and `workspace` force it to be adopted. `float` and `tile` also set whether the Client floats. `workspace` puts the Client on that Workspace; a trailing `silent` does not switch to it. `min_size` and `max_size` set the minimum and maximum track size, as two integers in pixels. `no_max_size` drops the maximum the window reported; a matching `max_size` still applies. `scrolling_width` sets the fraction of the Monitor used by the column a new tiled Client opens in. `pin` pins a floating Client so it stays shown on every Workspace of its Monitor. A trailing `0` turns that off. Pinning is ignored when the Client is tiled. The last matching rule wins when two rules disagree; rules that set different things combine.

`noinitialfocus` and `no_initial_focus` keep a new Client from taking focus. `nofocus` and `no_focus` refuse focus. `stayfocused` and `stay_focused` keep the focused Client focused when another window would take it. A trailing `0` turns that effect off. The last matching rule wins.

`misc:focus_on_activate` defaults to false. An application that takes the foreground without a recent click or key is sent back to the previous Client, and that application is reported as urgent when it is a Client. True accepts the change. A window that only flashes for attention, without taking the foreground, is not observed.

## Special workspaces

`special` and `special:` address the special workspace named `special:special`. `special:name` addresses `special:name`. The same address works in `workspace`, `movetoworkspace`, `movetoworkspacesilent`, and a window rule's `workspace` effect. A trailing `silent` on the rule does not show the special workspace.

`togglespecialworkspace` shows that special workspace on the focused Monitor, or hides it when it is already shown there. The argument is the name, and an empty name is `special`. `workspace special:name` shows it and does not hide it again. One special workspace is shown on a Monitor, over the normal Workspace, whose Clients stay shown. Showing a special workspace that is open on another Monitor moves it. The normal Workspace stays the active one.

`misc:close_special_on_empty` defaults to true. The special workspace closes when its last Client leaves. False leaves it open. `binds:hide_special_on_workspace_change` defaults to false. True hides the Monitor's special workspace when its normal Workspace changes. `input:special_fallthrough` defaults to false. Focusing a Client on the normal Workspace then closes the special workspace. True leaves the special workspace open and the focus moves. A pointer hit is not taken away from a window underneath a special workspace, so a click there still focuses that window and the option decides whether the special workspace closes.

Opening a special workspace emits `activespecial` and `activespecialv2` as `name,monitor` and `id,name,monitor`. Closing one emits them with an empty name. The unnamed special workspace is id -99. Another special workspace is -98, then -97, in name order. `special_scale_factor` stays a Gap.

A window rule that matches on `xwayland` is a Gap: the rule is skipped and the config warns. Other effects and match fields are skipped with a warning until a later change honors them.

A `workspace` rule is honored for `monitor`, `default`, `persistent`, `defaultName`, `on-created-empty`, `gapsin`, `gapsout`, `bordersize`, `border`, `rounding`, `decorate`, and `layoutopt` when that option is a dwindle option that is already honored. A Workspace bound to a Monitor is shown there. A persistent Workspace stays when it is empty. `default` selects the Workspace a Monitor shows when it has none. `defaultName` is the name reported for that Workspace. `on-created-empty` runs when the Workspace is created with no Clients, and the command is aimed at that Workspace. `gapsin`, `gapsout`, and `bordersize` replace the general values on that Workspace. `border:0` and `decorate:0` draw no border. `rounding:0` forces square corners. Later rules for the same Workspace override earlier ones.

`shadow` and `animation` on a workspace rule stay the `shadow` and `animation` Gaps. A `layout` field is not applied. Floating gaps on a workspace rule stay the `float_gaps` Gap. A `layoutopt` whose option is already a Gap warns and is not applied. Any other field is skipped.

A workspace filter such as `r[1-3]` matches workspaces that already exist. It is not a workspace address, so `workspace`, `movetoworkspace` and `movetoworkspacesilent` do not switch on it.

## Monitors

A `monitor` position is in logical pixels. Windows reports the monitor rectangle in physical pixels. The layout width is that physical width divided by the monitor's scale, rounded half away from zero. A scale-2 monitor that is 3840 physical pixels wide is 1920 wide in the layout. The position that is stored is that logical position multiplied by the monitor's scale, because window placement stays in physical pixels.

`reserved_area` and `addreserved` take top, bottom, left and right, in logical pixels, and subtract them from the work area. Values below zero count as zero.

`disable` and `disabled` remove the monitor from the layout. The display stays powered on.

Transforms 0, 1, 2 and 3 ask for a rotation of 0, 90, 180 and 270 degrees. Transforms 4, 5, 6 and 7 flip the picture. They warn "Flipped monitor transforms are not honored." and are not applied.

Resolution, refresh rate and scale changes stay Gaps. So do mirroring, variable refresh, bit depth, HDR and color management, including SDR brightness, SDR saturation and an ICC profile.

## Dispatchers

`forcerendererreload` returns ok and does nothing. Reloading a renderer has no Windows meaning.

A window selector `tag:` parses and matches no Client. Window tags are not tracked.

`swapwindow` exchanges two Clients that share a Workspace. A selector that matches a Client on another Workspace does not swap.

`dpms` asks for display power on, off, or toggled. An empty monitor list addresses every display. A monitor name that matches none of the enabled monitors still addresses every enabled monitor. The adapter broadcasts that request; tests do not send it.

`binds:window_direction_monitor_fallback` defaults to true. `movefocus` continues onto the neighbouring Monitor when nothing in that direction can take focus. `movewindow` continues when the point just outside the Client lies on another Monitor. Master, monocle and scrolling also continue when the move cannot be made inside the Workspace. False stops at the Monitor edge.

`focusmonitor`, `movecurrentworkspacetomonitor`, `moveworkspacetomonitor` and `swapactiveworkspaces` address a Monitor by its name, a `desc:` prefix of its description, a direction, `current`, a relative `+N` or `-N`, or its id. Changing the focused Monitor emits `focusedmon`. Moving a Workspace onto another Monitor emits `moveworkspace`.

A pinned Client moves with its Workspace onto the destination Monitor. It is not left on the source Monitor. Moving or swapping a normal Workspace does not move the special Workspace shown on that Monitor. The cursor is not forced to the center of the destination Monitor. Warps follow the cursor settings.

`pin` toggles whether a floating Client is pinned. `enable` and `on` pin it. `disable` and `off` unpin it. A window selector after that word, or in place of it, chooses the Client. A tiled Client does not qualify. Changing the pin emits `pin` with the address and `1` or `0`. The same request again emits nothing. A pinned Client moves onto the Workspace its Monitor is showing, and it is not hidden when that Monitor switches Workspace. Unpinning leaves it on that one Workspace. A pinned Client that is focused stays focused across the switch. Changing whether a Client floats clears the pin and does not emit `pin`.

`binds:allow_pin_fullscreen` is a Gap. Fullscreen does not keep or restore a pin. A pinned Client is not raised above other windows. Placement does not change z-order. A window owned by a pinned Client is not pinned with it.

## Fullscreen

`fullscreen` toggles the focused Client. An argument of `1`, `maximize`, or `maximized` toggles maximize. Any other argument, including `0` and `2`, toggles fullscreen. A second argument `set` or `unset` forces that mode on or off. An unrecognized second argument does nothing. `maximize` is the same toggle as `fullscreen 1`.

Mode 0 is the layout slot. Mode 1 resizes the Client to the monitor work area and keeps its border. Mode 2 resizes it to the monitor rectangle and drops the border. While a Client is in mode 1 or 2, the dwindle tree is not refit, and mode 0 restores that same slot. One covering Client replaces another on the same Workspace. Moving that Workspace onto another Monitor resizes the covering Client to the destination.

`fullscreenstate` takes an internal mode and a client mode. `-1` keeps the current value. Any other value is clamped to 0, 1, or 2, so a 3 is fullscreen. Passing the pair the Client already has turns both modes off. The internal mode is the placement above. The client mode is stored and reported as `fullscreenClient`. It is not sent to the application. Windows has no fullscreen state separate from the window rectangle, so a client mode by itself does not move the Client and cannot make an application believe it is fullscreen.

`sync_fullscreen` is not honored. The `fullscreen` and `maximize` dispatchers store the client mode equal to the internal mode. `fullscreenstate` stores them separately. A window rule `suppress_event` is not honored. An application that asks to be fullscreen or maximized is not observed, so a maximized Client does not return to maximized after the application leaves fullscreen on its own.

A `fullscreen` window rule requests mode 2. A `maximize` window rule requests mode 1. A trailing `0` turns that flag off. `fullscreen_state` sets the two modes, for example `2 0`, and wins over those flags. A popup window with no caption that already covers its monitor is not adopted.

## Commands

These commands are recognized and answer with an error. They are not honored.

| command | status | reason |
| --- | --- | --- |
| dismissnotify | error | There is no notification surface. |
| kill | error | Click to kill is not honored. |
| notify | error | There is no notification surface. |
| rollinglog | error | There is no rolling log. |
| setprop pin | error | The pin dispatcher and the pin window rule honor pinning. This command does not. |

`getprop` and `setprop` honor `float`. `getprop` reports `pin` as false. `setprop pin` is an error. Any other window property is not honored.

## Events

These events are not emitted. Each one waits on a feature this tree does not have yet.

`windowtitle` and `windowtitlev2` wait on a title change after a window is adopted. The title is read once, when the window appears.

`renameworkspace` waits on renaming a workspace. A workspace keeps the name it was created with.

## Winland notes

`code:N` is the XKB keycode, which is the evdev keycode plus 8. It names a physical key, so it does not follow the active layout. A config is resolved when it is read. Character names such as `ä` and `adiaeresis` follow the layout that is active when the key is pressed. A virtual-key alias such as `bracketleft` is resolved against the layout active when the config is read.

Windows scan codes below are set 1. Extended means the extended-key flag. Pause uses the same scan as num lock; the virtual key tells them apart. The synthetic left control of an AltGr press (scan `0x21D`, or left control with the extended flag) is not a key in this table.

| key | scan | extended | evdev | code |
| --- | --- | --- | --- | --- |
| Escape | 0x01 | no | 1 | 9 |
| 1 | 0x02 | no | 2 | 10 |
| 2 | 0x03 | no | 3 | 11 |
| 3 | 0x04 | no | 4 | 12 |
| 4 | 0x05 | no | 5 | 13 |
| 5 | 0x06 | no | 6 | 14 |
| 6 | 0x07 | no | 7 | 15 |
| 7 | 0x08 | no | 8 | 16 |
| 8 | 0x09 | no | 9 | 17 |
| 9 | 0x0A | no | 10 | 18 |
| 0 | 0x0B | no | 11 | 19 |
| Q | 0x10 | no | 16 | 24 |
| W | 0x11 | no | 17 | 25 |
| E | 0x12 | no | 18 | 26 |
| R | 0x13 | no | 19 | 27 |
| T | 0x14 | no | 20 | 28 |
| Y | 0x15 | no | 21 | 29 |
| U | 0x16 | no | 22 | 30 |
| I | 0x17 | no | 23 | 31 |
| O | 0x18 | no | 24 | 32 |
| P | 0x19 | no | 25 | 33 |
| A | 0x1E | no | 30 | 38 |
| S | 0x1F | no | 31 | 39 |
| D | 0x20 | no | 32 | 40 |
| F | 0x21 | no | 33 | 41 |
| G | 0x22 | no | 34 | 42 |
| H | 0x23 | no | 35 | 43 |
| J | 0x24 | no | 36 | 44 |
| K | 0x25 | no | 37 | 45 |
| L | 0x26 | no | 38 | 46 |
| Z | 0x2C | no | 44 | 52 |
| X | 0x2D | no | 45 | 53 |
| C | 0x2E | no | 46 | 54 |
| V | 0x2F | no | 47 | 55 |
| B | 0x30 | no | 48 | 56 |
| N | 0x31 | no | 49 | 57 |
| M | 0x32 | no | 50 | 58 |
| F1 | 0x3B | no | 59 | 67 |
| F2 | 0x3C | no | 60 | 68 |
| F3 | 0x3D | no | 61 | 69 |
| F4 | 0x3E | no | 62 | 70 |
| F5 | 0x3F | no | 63 | 71 |
| F6 | 0x40 | no | 64 | 72 |
| F7 | 0x41 | no | 65 | 73 |
| F8 | 0x42 | no | 66 | 74 |
| F9 | 0x43 | no | 67 | 75 |
| F10 | 0x44 | no | 68 | 76 |
| F11 | 0x57 | no | 87 | 95 |
| F12 | 0x58 | no | 88 | 96 |
| Space | 0x39 | no | 57 | 65 |
| Tab | 0x0F | no | 15 | 23 |
| Return | 0x1C | no | 28 | 36 |
| BackSpace | 0x0E | no | 14 | 22 |
| Shift_L | 0x2A | no | 42 | 50 |
| Shift_R | 0x36 | no | 54 | 62 |
| Control_L | 0x1D | no | 29 | 37 |
| Alt_L | 0x38 | no | 56 | 64 |
| KP_0 | 0x52 | no | 82 | 90 |
| KP_1 | 0x4F | no | 79 | 87 |
| KP_2 | 0x50 | no | 80 | 88 |
| KP_3 | 0x51 | no | 81 | 89 |
| KP_4 | 0x4B | no | 75 | 83 |
| KP_5 | 0x4C | no | 76 | 84 |
| KP_6 | 0x4D | no | 77 | 85 |
| KP_7 | 0x47 | no | 71 | 79 |
| KP_8 | 0x48 | no | 72 | 80 |
| KP_9 | 0x49 | no | 73 | 81 |
| KP_Multiply | 0x37 | no | 55 | 63 |
| KP_Subtract | 0x4A | no | 74 | 82 |
| KP_Add | 0x4E | no | 78 | 86 |
| KP_Decimal | 0x53 | no | 83 | 91 |
| Scroll_Lock | 0x46 | no | 70 | 78 |
| Num_Lock | 0x45 | no | 69 | 77 |
| Pause | 0x45 | no | 119 | 127 |
| Up | 0x48 | yes | 103 | 111 |
| Down | 0x50 | yes | 108 | 116 |
| Left | 0x4B | yes | 105 | 113 |
| Right | 0x4D | yes | 106 | 114 |
| Home | 0x47 | yes | 102 | 110 |
| End | 0x4F | yes | 107 | 115 |
| Prior | 0x49 | yes | 104 | 112 |
| Next | 0x51 | yes | 109 | 117 |
| Insert | 0x52 | yes | 110 | 118 |
| Delete | 0x53 | yes | 111 | 119 |
| Print | 0x37 | yes | 99 | 107 |
| Menu | 0x5D | yes | 127 | 135 |
| KP_Enter | 0x1C | yes | 96 | 104 |
| KP_Divide | 0x35 | yes | 98 | 106 |
| Control_R | 0x1D | yes | 97 | 105 |
| Alt_R | 0x38 | yes | 100 | 108 |
| Super_L | 0x5B | yes | 125 | 133 |
| Super_R | 0x5C | yes | 126 | 134 |

F13 through F24 have no single set-1 scan code in this table. Enter is `code:36`. `code:28` is the T position, not Enter.

## Plugins

`plugin` loads an in-process plugin assembly and is honored, so it is not a Gap. A plugin may register dispatchers, config keywords and event handlers. Registering a Layout is refused until a later change. Installing a plugin from a repository is not part of this.
