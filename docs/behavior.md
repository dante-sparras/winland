# Behavior

How Winland behaves for the options, dispatchers and window rules it honors. What it does not honor is in [gaps.md](gaps.md), what it adds to Hyprland is in [extensions.md](extensions.md), and terms are defined in [CONTEXT.md](../CONTEXT.md).

## Layout

`general:layout` is honored for `dwindle`, `master`, `monocle`, and `scrolling`. Any other value warns with its line number. Dwindle still runs when the named layout is not available.

## Decoration

`decoration:rounding` selects square corners when the value is 0 and rounded corners otherwise. The exact radius is not applied.

A border color is one color, written as `rgba()`, `rgb()`, or `0xAARRGGBB`. A gradient keeps the first color. Gradient borders are not honored.

`decoration:active_opacity`, `decoration:inactive_opacity` and `decoration:fullscreen_opacity` are fractions from 0 to 1 and default to 1. A value outside that range is clamped. A Client in fullscreen mode 2 uses `fullscreen_opacity`. A maximized Client uses the active or inactive fraction, and so does every other Client.

An `opacity` window rule supplies up to three fractions: active, inactive and fullscreen. One fraction is used for all three. Two fractions set active and inactive and leave fullscreen at 1, which then multiplies the configured fullscreen fraction. Each fraction multiplies the configured fraction unless the next token is `override`, which uses the rule's fraction alone. The last matching `opacity` rule wins. `opaque` forces the fraction to 1, and a trailing `0` turns that off. The last matching `opaque` rule wins.

`decoration:dim_inactive` defaults to false and `decoration:dim_strength` defaults to 0.5. When dimming is on, an inactive Client's fraction is multiplied by `1 - dim_strength`. Inactive windows are dimmed by tinting them toward black. That tint cannot be drawn on a window here, so dimming lowers the window's alpha instead. `decoration:dim_modal` defaults to true. A Client shadowed by a modal moves that dim halfway toward 1 before the alpha is taken. Modal parents are not tracked yet, so the extra dim does not apply. `decoration:dim_special` defaults to 0.2 and multiplies the alpha of Clients on a Monitor whose special workspace is open, except the Clients on that special workspace. `decoration:dim_around` is a Gap. Blur and shadow stay Gaps.

An alpha below 255 adds `WS_EX_LAYERED` and sets the window's alpha. The window's previous extended style is restored when it stops being a Client, on a clean exit, and after a crash for every window this program layered. A window that was already layered is left alone. A window that refuses the layered style is skipped.

## Exec rule blocks

A rule block on `exec`, `exec-once`, `execr`, `execr-once`, `exec-shutdown`, or the `exec` and `execr` dispatchers applies to the first window mapped by that process or a child of it. `float`, `tile`, `workspace`, `ignore`, `opacity`, `opaque`, and `pin` are honored. Any other effect, including `move`, `size`, and `center`, warns and is skipped. The command still launches. An unclosed rule block warns and does not launch. A known effect with a bad argument warns and does not launch.

## Border resize and floating snap

`general:resize_on_border` defaults to false. When it is on, a press in the grab ring around a Client resizes that Client. The ring is `general:border_size` plus `general:extend_border_grab_area` (0 to 100, default 15) outside the Client. A press inside the Client is left to the Client. A rounded corner is not a grab: the corner radius is not applied, so that part of the window stays a normal click.

`general:hover_icon_on_border` defaults to true and is used only while border resize is on. The outcome names the cursor for the edge under the pointer: `top_side`, `bottom_side`, `left_side`, `right_side`, `top_left_corner`, `top_right_corner`, `bottom_left_corner`, or `bottom_right_corner`. The corner zone is the border plus 10 pixels. The system pointer is not changed.

`general:resize_corner` defaults to 0. For a floating Client, 1, 2, 3, and 4 force the grab to the top left, top right, bottom right, and bottom left. 0 uses the corner nearest the pointer. Names `disable`, `top_left`, `top_right`, `bottom_right`, and `bottom_left` are the same values.

`general:float_gaps` is the gap between a floating Client and the monitor edges, as one to four integers, default 0. A negative side uses `general:gaps_out` instead. Floating Clients are kept inside that inset.

`general:snap:enabled` defaults to false. While a floating Client is moved or resized, `general:snap:window_gap` (default 10) snaps it to another Client and `general:snap:monitor_gap` (default 10) snaps it to the monitor. Both are 0 to 100, and 0 turns that snap off. `general:snap:border_overlap` defaults to false. When it is on, the snap meets the monitor so the border overlaps the edge. `general:snap:respect_gaps` defaults to false. When it is on, window snaps leave `general:gaps_in` between Clients and monitor snaps leave `general:gaps_out` at the edges.

## Mouse drag

`bindm` binds a mouse button to `movewindow` or `resizewindow`. A press takes the Client under the pointer, focuses it, and drags it until that button is released. The `mouse` dispatcher in an ordinary bind, as in `bind = SUPER, mouse:272, mouse, movewindow`, is the same action. It needs a mouse button and takes only `movewindow` or `resizewindow`. A press with no Client under the pointer does not drag.

`binds:drag_threshold` defaults to 0. Above 0, the Client is not picked up until the pointer has moved farther than that many pixels from the press. The drag then starts from that point, so the Client does not jump. The same distance separates the `c` and `g` bind flags.

`movewindow` moves a floating Client with the pointer. A tiled Client is lifted out of the Layout and floats at its tiled size, centered on the pointer. On release it drops back into the Layout at the pointer. In dwindle it splits the tile under the pointer, or the nearest tile, and goes on the half the pointer is over. `dwindle:force_split` does not apply to a drop. `dwindle:precise_mouse_move` defaults to false. When it is on, a drop picks the side of the tile nearest the pointer, as `dwindle:smart_split` does, and splits keep their direction when the tree is refit.

`resizewindow` resizes a floating Client from the grabbed corner. That is the corner nearest the pointer, or the one `general:resize_corner` forces. A tiled Client changes its split ratios instead. `resizewindow 1` keeps a floating Client's aspect ratio: the side that grew more sets the size and the other follows. `resizewindow 2` and no argument do not keep it.

`dwindle:smart_resizing` defaults to true. A mouse resize of a tile moves the edges on the grabbed corner's side and keeps the opposite edges in place, so the tiles on the far side do not change. A tile against a monitor edge moves its other edge. `resizeactive` has no corner and moves the nearest split on each axis. False always moves the nearest split, whichever corner is grabbed. `master:smart_resizing` defaults to true. Grabbing a tile's top edge in a vertical stack, or its left edge in a horizontal one, takes the space from the tiles before it. In the center orientation a grabbed master moves both master edges.

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


## Special workspaces

`special` and `special:` address the special workspace named `special:special`. `special:name` addresses `special:name`. The same address works in `workspace`, `movetoworkspace`, `movetoworkspacesilent`, and a window rule's `workspace` effect. A trailing `silent` on the rule does not show the special workspace.

`togglespecialworkspace` shows that special workspace on the focused Monitor, or hides it when it is already shown there. The argument is the name, and an empty name is `special`. `workspace special:name` shows it and does not hide it again. One special workspace is shown on a Monitor, over the normal Workspace, whose Clients stay shown. Showing a special workspace that is open on another Monitor moves it. The normal Workspace stays the active one.

`misc:close_special_on_empty` defaults to true. The special workspace closes when its last Client leaves. False leaves it open. `binds:hide_special_on_workspace_change` defaults to false. True hides the Monitor's special workspace when its normal Workspace changes. `input:special_fallthrough` defaults to false. Focusing a Client on the normal Workspace then closes the special workspace. True leaves the special workspace open and the focus moves. A pointer hit is not taken away from a window underneath a special workspace, so a click there still focuses that window and the option decides whether the special workspace closes.

Opening a special workspace emits `activespecial` and `activespecialv2` as `name,monitor` and `id,name,monitor`. Closing one emits them with an empty name. The unnamed special workspace is id -99. Another special workspace is -98, then -97, in name order. `special_scale_factor` stays a Gap.

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
