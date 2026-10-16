# Behavior

How Winland behaves for the options, dispatchers and window rules it honors. What it does not honor is in [gaps.md](gaps.md), what it adds to Hyprland is in [extensions.md](extensions.md), and terms are defined in [CONTEXT.md](../CONTEXT.md).

## Layout

`general:layout` is honored for `dwindle`, `master`, `monocle`, and `scrolling`. Any other value warns with its line number. Dwindle still runs when the named layout is not available.

## Decoration

`decoration:rounding` is the corner radius in logical pixels and defaults to 0, which draws square corners. A negative value is ignored. `decoration:rounding_power` defaults to 2 and is clamped to 1 through 10: 1 cuts a straight corner, 2 a circle, and a larger value is squarer. The radius drawn is `rounding * rounding_power / 2`, multiplied by the scale of the Client's Monitor. A circle of radius 8 or 4 uses DWM's round or small round corner preference, which DWM scales with the window's DPI. Any other radius or power is cut from the window as a window region inside the visible frame, following `|x|^p + |y|^p = r^p` at each corner, and is cut again when the window changes size. A radius larger than half the shorter side is cut to that half. A Client in fullscreen mode 2 and a Client on a Workspace with `rounding:0` draw square corners. A window that already has a region of its own keeps it, and its corners are not cut.

`decoration:blur:enabled` defaults to true and gives every Client DWM's acrylic backdrop (`DWMSBT_TRANSIENTWINDOW`). False gives it no backdrop (`DWMSBT_NONE`). A window or a Windows build that refuses the backdrop is left as it is. `decoration:shadow:enabled` defaults to true and leaves DWM's shadow alone. False sets DWM's non-client rendering policy to disabled (`DWMNCRP_DISABLED`), so DWM draws no shadow and none of the rest of the window frame it draws.

The backdrop, the rendering policy and the region are put back when the window stops being a Client and on a clean exit. The backdrop goes back to the type the window had, or to the system choice when that cannot be read. The rendering policy goes back to following the window style.

A border color is one color, written as `rgba()`, `rgb()`, or `0xAARRGGBB`. A gradient keeps the first color. Gradient borders are not honored.

`decoration:active_opacity`, `decoration:inactive_opacity` and `decoration:fullscreen_opacity` are fractions from 0 to 1 and default to 1. A value outside that range is clamped. A Client in fullscreen mode 2 uses `fullscreen_opacity`. A maximized Client uses the active or inactive fraction, and so does every other Client.

An `opacity` window rule supplies up to three fractions: active, inactive and fullscreen. One fraction is used for all three. Two fractions set active and inactive and leave fullscreen at 1, which then multiplies the configured fullscreen fraction. Each fraction multiplies the configured fraction unless the next token is `override`, which uses the rule's fraction alone. The last matching `opacity` rule wins. `opaque` forces the fraction to 1, and a trailing `0` turns that off. The last matching `opaque` rule wins.

`decoration:dim_inactive` defaults to false and `decoration:dim_strength` defaults to 0.5. When dimming is on, an inactive Client's fraction is multiplied by `1 - dim_strength`. Inactive windows are dimmed by tinting them toward black. That tint cannot be drawn on a window here, so dimming lowers the window's alpha instead. `decoration:dim_modal` defaults to true. A Client shadowed by a modal moves that dim halfway toward 1 before the alpha is taken. Modal parents are not tracked yet, so the extra dim does not apply. `decoration:dim_special` defaults to 0.2 and multiplies the alpha of Clients on a Monitor whose special workspace is open, except the Clients on that special workspace. `decoration:dim_around` is a Gap.

An alpha below 255 adds `WS_EX_LAYERED` and sets the window's alpha. The window's previous extended style is restored when it stops being a Client, on a clean exit, and after a crash for every window this program layered. A window that was already layered is left alone. A window that refuses the layered style is skipped.

## Exec rule blocks

A rule block on `exec`, `exec-once`, `execr`, `execr-once`, `exec-shutdown`, or the `exec` and `execr` dispatchers applies to the first window mapped by that process or a child of it. Every effect a window rule honors is honored there too, including `move`, `size`, `center`, `monitor` and `no_close_for`. Any other effect warns and is skipped. The command still launches. An unclosed rule block warns and does not launch. A known effect with a bad argument warns and does not launch.

## Window placement

A new floating Client opens at the size it asked for, centered in the floating work area of its Monitor: the work area less `general:float_gaps`. A window that reports no size opens at 640 by 400. A tiled Client is placed by its Layout. A Client that comes back after a restart keeps the rectangle it had.

`size` and `move` take two expressions, x then y, split at the first whitespace, so an expression cannot contain a space. An expression is numbers, `+ - * / ^`, parentheses, a unary minus, the constants `_pi` and `_e`, the one-argument functions `abs`, `sqrt`, `sign`, `rint`, `exp`, `ln`, `log`, `log2`, `log10`, `sin`, `cos`, `tan`, `asin`, `acos`, `atan`, `sinh`, `cosh` and `tanh`, and the variables `monitor_w`, `monitor_h`, `window_x`, `window_y`, `window_w`, `window_h`, `cursor_x` and `cursor_y`. Positions are local to the Monitor the Client opens on. Numbers are physical pixels, like `min_size`. A rule that is not two expressions, or uses a name that is not listed, warns with its line number and is skipped. A `%` is not part of an expression, so `50%` warns. A value that cannot be computed, such as a division by zero, leaves the Client where it would open without the rule.

`size` sets a floating Client's size, then the minimum and maximum size clamp it. `window_w` and `window_h` are the size the window asked for. A tiled Client keeps that size as its pseudo size. `move` places a floating Client's top left corner, and `window_w` and `window_h` there are the size after `size`. `center` centers a floating Client in the floating work area, which is also where it opens without a rule. `move` and `center` replace each other: the last matching one wins. A trailing `0` turns `center` off. Neither moves a tiled Client.

`monitor` opens the window on that Monitor, before a `workspace` rule picks the Workspace. It takes the same selectors as `focusmonitor`. Without a trailing `silent` that Monitor is focused. `unset` and a Monitor that does not exist keep the Monitor the window opened on.

`no_close_for` takes milliseconds. Until that time has passed since the window opened, `killactive` and `closewindow` answer `error: can't close window, it's not closeable yet (noclosefor)` and do not ask it to close. `killwindow` still ends the process.

`movewindowpixel` and `resizewindowpixel` take two numbers, a comma and a window selector. The numbers are added to the window's position or size. After `exact` they replace it. A trailing `%` is a percentage of the position or size, or of the focused Monitor after `exact`. Text that is not two numbers leaves the window as it is. `movewindowpixel` moves a floating Client and leaves a tiled one. `resizewindowpixel` resizes through the Layout, so a floating Client grows from its center and a tiled Client changes its split. A size below one pixel answers `error: Invalid size`, and a fullscreen or maximized Client answers `error: Window is fullscreen`. A selector that matches nothing answers `error: moveWindow: no window` or `error: resizeWindow: no window`.

`centerwindow` centers the active floating Client in its Monitor's work area. A tiled or fullscreen Client answers `error: No floating window found`. Its argument is ignored.

`bringactivetotop` raises the active Client above every window that is not topmost. `alterzorder` takes `top` or `bottom`, a comma and a window selector. Without a match it uses the active Client when that Client floats, and otherwise does nothing. Any other position answers `error: Bad z-order position: <position>`. The adapter changes the z-order with `SetWindowPos` and `HWND_TOP` or `HWND_BOTTOM`, without moving, sizing or activating the window.

`movecursortocorner` warps the cursor to a corner of the active Client: 0 bottom left, 1 bottom right, 2 top right, 3 top left. Any other number answers `error: Corner must be 0 - 3`, and text that is not a number answers `error: moveCursorToCorner, arg has to be a number`.

## Border resize and floating snap

`general:resize_on_border` defaults to false. When it is on, a press in the grab ring around a Client resizes that Client. The ring is `general:border_size` plus `general:extend_border_grab_area` (0 to 100, default 15) outside the Client. A press inside the Client is left to the Client. A rounded corner is not a grab: the ring is measured from the square frame.

`general:hover_icon_on_border` defaults to true and is used only while border resize is on. The outcome names the cursor for the edge under the pointer: `top_side`, `bottom_side`, `left_side`, `right_side`, `top_left_corner`, `top_right_corner`, `bottom_left_corner`, or `bottom_right_corner`. The corner zone is the border plus 10 pixels. The system pointer is not changed.

`general:resize_corner` defaults to 0. For a floating Client, 1, 2, 3, and 4 force the grab to the top left, top right, bottom right, and bottom left. 0 uses the corner nearest the pointer. Names `disable`, `top_left`, `top_right`, `bottom_right`, and `bottom_left` are the same values.

`general:float_gaps` is the gap between a floating Client and the monitor edges, as one to four integers, default 0. A negative side uses `general:gaps_out` instead. Floating Clients are kept inside that inset.

`general:snap:enabled` defaults to false. While a floating Client is moved or resized, `general:snap:window_gap` (default 10) snaps it to another Client and `general:snap:monitor_gap` (default 10) snaps it to the monitor. Both are 0 to 100, and 0 turns that snap off. `general:snap:border_overlap` defaults to false. When it is on, the snap meets the monitor so the border overlaps the edge. `general:snap:respect_gaps` defaults to false. When it is on, window snaps leave `general:gaps_in` between Clients and monitor snaps leave `general:gaps_out` at the edges.

## Mouse drag

`bindm` binds a mouse button to `movewindow` or `resizewindow`. A press takes the Client under the pointer, focuses it, and drags it until that button is released. The `mouse` dispatcher in an ordinary bind, as in `bind = SUPER, mouse:272, mouse, movewindow`, is the same action. It needs a mouse button and takes only `movewindow` or `resizewindow`. A press with no Client under the pointer does not drag.

`binds:drag_threshold` defaults to 0. Above 0, the Client is not picked up until the pointer has moved farther than that many pixels from the press. The drag then starts from that point, so the Client does not jump. The same distance separates the `c` and `g` bind flags.

`movewindow` moves a floating Client with the pointer. A tiled Client is lifted out of the Layout and floats at its tiled size, centered on the pointer. On release it drops back into the Layout at the pointer. In dwindle it splits the tile under the pointer, or the nearest tile, and goes on the half the pointer is over. `dwindle:force_split` does not apply to a drop. `dwindle:precise_mouse_move` defaults to false. When it is on, a drop picks the side of the tile nearest the pointer, as `dwindle:smart_split` does, and splits keep their direction when the tree is refit.

A Client that the system moves by its title bar is dragged the same way. A tiled Client is lifted out of its Layout when the move starts, and the other tiles close the gap. When the move ends it is dropped back in where the pointer is, by the same rule as a `movewindow` drop in that Layout. A maximized or fullscreen Client leaves that mode when its title is taken. A floating Client stays where it is dropped. A drag that starts on a sizing border is a resize: a tiled Client changes its split when the border is let go, as `resizewindow` does, and a maximized or fullscreen Client goes back to covering the area. Hyprland does the same for the move and resize requests of a client.

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

On a Workspace with a maximized or fullscreen Client, that Client owns the pointer. The tiles behind it take no focus when the pointer passes over them. A maximized Client takes the focus only where it is drawn, and a fullscreen one wherever the pointer is.

`input:mouse_refocus` defaults to true. When it is false, mode 1 does not move keyboard focus back to the Client already under the pointer until the pointer crosses onto another Client.

`input:float_switch_override_focus` defaults to 1. In modes 0 and 2, moving between a tiled Client and a floating Client moves keyboard focus. 2 also does so between two floating Clients. 0 does not. Mode 3 does not use it.

`input:focus_on_close` defaults to 0. 0 focuses the nearest remaining tiled Client, or the most recently focused Client when the one that closed was floating. 1 focuses the Client under the pointer. 2 focuses the most recently focused Client. The names `next`, `cursor`, and `mru` are 0, 1, and 2. A master layout with `master:focus_master_on_close` still focuses the master when the closed Client was tiled and the mode is 0.

`misc:mouse_move_focuses_monitor` defaults to true. Moving the pointer onto another Monitor focuses that Monitor. False leaves it until a Client there takes keyboard focus.

Modes 2 and 3 cannot choose which window receives pointer input. The window under the pointer still receives it. Those modes only decide keyboard focus.


## Touchpad swipe

`gesture = fingers, direction, action` is honored for the `workspace` action, so `gesture = 3, horizontal, workspace` switches Workspace with a three-finger swipe. Fingers run from 2 to 9, and the directions are `swipe`, `horizontal`, `vertical`, `left`, `right`, `up` and `down`, with the short names the pinned release accepts. `mod:` takes the modifiers that must be held, and `scale:` multiplies the distance, clamped from 0.1 to 10. A gesture matches once the fingers have moved 5 pixels: the larger axis and its sign pick the direction, and the first gesture whose direction, finger count and held modifiers all match takes the swipe. A gesture shadowed by an earlier one warns and is skipped, and `unset` removes the gesture with the same fingers, direction, modifiers, scale and flag. A swipe is a touchpad gesture of three or more fingers, so a two-finger gesture never matches, as in the pinned release. A locked session does not swipe.

The swipe follows the configured direction, not the one the fingers took. `horizontal` reads rightward travel, `vertical` downward, `left` leftward, `up` upward and `swipe` the length of the movement. `gestures:workspace_swipe_invert` defaults to true, which turns that around, so fingers moving left go to the next Workspace and fingers moving right go to the previous one. The next and previous Workspaces are `m+1` and `m-1` on the focused Monitor, or `r+1` and `r-1` with `gestures:workspace_swipe_use_r`. There is no previous Workspace before the first.

`gestures:workspace_swipe_distance` is the travel of a full swipe, in pixels from 0 to 2000, default 300. A touchpad's travel is counted at about 300 pixels per inch, 12 per millimetre. When the fingers lift, the Workspace changes once the swipe has covered `gestures:workspace_swipe_cancel_ratio` of that distance (0 to 1, default 0.5), or when its average speed per update is at least `gestures:workspace_swipe_min_speed_to_force` (0 to 200, default 30, where 0 never forces). A swipe shorter than 2 pixels never changes the Workspace. `gestures:workspace_swipe_create_new` defaults to true. Swiping on past the last Workspace then creates the next one, unless the Workspace swiped from is empty. False stops at the last Workspace, and a Monitor with one Workspace does not swipe at all. `gestures:workspace_swipe_direction_lock` defaults to true. Once the swipe has moved `gestures:workspace_swipe_direction_lock_threshold` pixels (0 to 200, default 10), moving back the other way counts as no travel. `gestures:workspace_swipe_forever` defaults to false. True switches each time the swipe covers the full distance and keeps going from there.

Winland reads the fingers as raw input from a precision touchpad. A touchpad that is not a precision touchpad does not swipe. Raw input cannot be consumed, so Windows also acts on the same fingers. Turn its gestures off for the finger count you configure: open Settings, then Bluetooth & devices, then Touchpad, expand Three-finger gestures (or Four-finger gestures), and set Swipes to Nothing. Otherwise a three-finger swipe both switches the Winland Workspace and switches apps or desktops in Windows.

## Special workspaces

`special` and `special:` address the special workspace named `special:special`. `special:name` addresses `special:name`. The same address works in `workspace`, `movetoworkspace`, `movetoworkspacesilent`, and a window rule's `workspace` effect. A trailing `silent` on the rule does not show the special workspace.

`togglespecialworkspace` shows that special workspace on the focused Monitor, or hides it when it is already shown there. The argument is the name, and an empty name is `special`. `workspace special:name` shows it and does not hide it again. One special workspace is shown on a Monitor, over the normal Workspace, whose Clients stay shown. Showing a special workspace that is open on another Monitor moves it. The normal Workspace stays the active one.

`misc:close_special_on_empty` defaults to true. The special workspace closes when its last Client leaves. False leaves it open. `binds:hide_special_on_workspace_change` defaults to false. True hides the Monitor's special workspace when its normal Workspace changes. `input:special_fallthrough` defaults to false. Focusing a Client on the normal Workspace then closes the special workspace. True leaves the special workspace open and the focus moves. A pointer hit is not taken away from a window underneath a special workspace, so a click there still focuses that window and the option decides whether the special workspace closes.

Opening a special workspace emits `activespecial` and `activespecialv2` as `name,monitor` and `id,name,monitor`. Closing one emits them with an empty name. The unnamed special workspace is id -99. Another special workspace is -98, then -97, in name order. `special_scale_factor` stays a Gap.

A `workspace` rule is honored for `monitor`, `default`, `persistent`, `defaultName`, `on-created-empty`, `gapsin`, `gapsout`, `bordersize`, `border`, `rounding`, `decorate`, and `layoutopt` when that option is a dwindle option that is already honored. A Workspace bound to a Monitor is shown there. A persistent Workspace stays when it is empty. `default` selects the Workspace a Monitor shows when it has none. `defaultName` is the name reported for that Workspace. `on-created-empty` runs when the Workspace is created with no Clients, and the command is aimed at that Workspace. `gapsin`, `gapsout`, and `bordersize` replace the general values on that Workspace. `border:0` and `decorate:0` draw no border. `rounding:0` forces square corners. Later rules for the same Workspace override earlier ones.

`shadow` on a workspace rule warns and is not applied. `animation` on a workspace rule stays the `animation` Gap. A `layout` field is not applied. Floating gaps on a workspace rule stay the `float_gaps` Gap. A `layoutopt` whose option is already a Gap warns and is not applied. Any other field is skipped.

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

`sync_fullscreen` is not honored. The `fullscreen` and `maximize` dispatchers store the client mode equal to the internal mode. `fullscreenstate` stores them separately. A window rule `suppress_event` is not honored. An application that asks to be fullscreen is not observed, so a maximized Client does not return to maximized after the application leaves fullscreen on its own.

Hyprland has no minimize, so Winland has none either. When a Client is minimized, by its button, `Win+Down`, `Win+M` or a double click, Winland restores it at once and the Client keeps its place. A window that is already minimized when Winland starts is not adopted until it is restored, and a minimized window is not put back into the Layout when Winland restarts.

A Client's maximize request is maximize mode 1, as in Hyprland: its maximize button, `Win+Up` or a double click on the title bar maximizes it, and asking again leaves it. It acts on that Client, whichever window has focus. The system's own maximized state is undone, so the Client fills the work area as `fullscreen 1` does. A window that was maximized before Winland started is adopted maximized.

A `fullscreen` window rule requests mode 2. A `maximize` window rule requests mode 1. A trailing `0` turns that flag off. `fullscreen_state` sets the two modes, for example `2 0`, and wins over those flags. A popup window with no caption that already covers its monitor is not adopted.

## Plugins

`winlandctl plugin install <git-url> [revision]` clones the repository and builds the plugins its manifest lists, then installs them. A repository that is already installed is refused. A revision checks out that commit, branch or tag. `update` rebuilds a repository only when its commit changed, or every repository with `--force`. `remove` takes a repository URL or one of its plugin names and removes the whole repository. `list --installed` lists the installed repositories and their plugins. The manifest, the build and the approval are in [plugins.md](plugins.md).

## Sending keys

`pass` sends the key of the bind that ran it to the Client the window selector names. The press goes on when the key goes down and the release when it comes up, so a `pass` bind runs on the release too without the `r` flag. The modifiers the user holds are left as they are. A selector that matches no Client answers `pass: window not found`.

`sendshortcut` takes modifiers, a key, and a window selector. It sends the key with exactly those modifiers down. A modifier the user holds that is not named is let go around the key and pressed again after it. A `sendshortcut` bind presses the key on the press and releases it on the release. From the pipe it presses and releases at once. A number above 9 and `code:N` are keycodes, `mouse:272` to `mouse:276` are the left, right, middle, side and extra buttons, and anything else is a key name in the focused window's keyboard layout. An empty selector sends to the focused window. A bad argument answers `invalid args`, `invalid modifiers`, `invalid mouse button`, `invalid key`, `key not found`, or `window not found`, after the dispatcher name.

`sendkeystate` takes modifiers, a key, a state, and a window selector. `down` presses the key, `repeat` presses it twice, and `up` releases it. Any other state is an error.

A sent key is tagged, so Winland's own hooks let it through and it never runs a bind.

## Urgency

A Client is urgent when it flashes for attention, and when it takes the foreground and `misc:focus_on_activate` sends the focus back. Each emits `urgent` with the Client's address. The focused Client flashing is not urgent. Focusing a Client clears its urgency.

`focusurgentorlast` focuses the first urgent Client in Workspace order and switches to its Workspace. With none, it focuses the Client focused before the current one, or the Client focused last when none is focused. With neither it answers `error: window not found`.

## Swallowing

`misc:enable_swallow` defaults to false. `misc:swallow_regex` is the class a parent Client must match in full, and an empty value swallows nothing. `misc:swallow_exception_regex` is a title that keeps a parent from being swallowed. With swallowing on, a new Client whose process descends from a parent Client's process swallows that parent: the parent leaves its Layout, so the child takes its place, and the parent is hidden. When more than one parent matches, the one focused most recently is swallowed. A parent that is already swallowed is not swallowed by a second child. The process tree is read up to 16 levels.

The child reports its parent as `swallowing` in `clients`. The parent stays in `clients` as hidden, on the child's Workspace. When the child closes, including when its process ends without closing it, the parent returns to the child's Workspace, is shown, and takes the focus if the child had it. A parent that shows itself while swallowed is hidden again. A parent that closes while swallowed is forgotten.

A swallowed parent is a hidden Client, so it is shown on a clean exit, and after a crash it is shown on the next start. A child still open at that start swallows it again.

`toggleswallow` shows the parent of the focused Client, or hides it again. A parent shown this way stays when the child closes.

## Idle inhibit

The `idle_inhibit` window rule takes `none`, `always`, `focus`, or `fullscreen`. `always` keeps the system and the display awake while the Client is open, on any Workspace. `focus` does so while it is the focused Client. `fullscreen` does so while it is fullscreen or maximized on a shown Workspace. `none` does nothing. Any other mode warns and the rule is skipped. The last matching rule wins. `clients` reports `inhibitingIdle` for each Client.

## Animations

`animations:enabled` defaults to true. False, or the Windows setting **Animation effects** turned off, moves every window at once. `animations:workspace_wraparound` defaults to false. When it is on, a switch between the lowest and highest numbered Workspaces slides as if they were neighbours.

`bezier = NAME, X0, Y0, X1, Y1` names a cubic Bézier curve. `default` (0, 0.75, 0.15, 1) and `linear` are always defined. `animation = NAME, ONOFF, SPEED, CURVE[, STYLE]` sets one node of the animation tree. SPEED is in tenths of a second. A node that is not set takes the values of its nearest set parent, and `global` starts at on, 8, `default`. Both lines are read in config order and may also be written inside `animations { }`. A missing node, a speed that is not above 0, or a curve not defined above the line warns and is skipped. A style that is not valid for its node warns and is still kept.

These nodes animate:

- `windowsIn` and `fadeIn` when a window is adopted. The default style `popin [N%]` grows it from N percent of its size, at least 5 pixels, around its middle. `slide [top|bottom|left|right]` brings it in from the nearest monitor edge or the side named, and `gnomed` unfolds it from a line across its middle. The window fades in from transparent.
- `windowsOut` and `fadeOut` when Winland closes a Client. The window plays the same style in reverse and fades out, and is then asked to close. A window that is still shown 1.5 seconds later is put back on its Layout rectangle at its full alpha.
- `windowsMove` when a shown Client gets a new Layout rectangle, from wherever it is at that moment.
- `workspacesIn` and `workspacesOut` when a Monitor switches Workspace, and `specialWorkspaceIn` and `specialWorkspaceOut` when a special workspace is shown or hidden. Styles are `slide`, `slidevert`, `fade`, `slidefade` and `slidefadevert`, with an optional travel percentage such as `slidefade 20%` and an optional side. A Workspace with a higher number comes in from the right. A special workspace also fades. A leaving window is hidden when its motion ends.
- `fadeSwitch` when a Client's alpha changes, for example between `active_opacity` and `inactive_opacity`.
- `border` when a Client's border color changes. The colors blend in the OkLab space.

The adapter moves each window with `SetWindowPos` and sets its alpha with `WS_EX_LAYERED`, once per composed frame, on a thread of its own, so binds and window events are never kept waiting. A window that has stopped answering skips the frames in between. A motion that starts while another runs carries on from where the window is. A placement that arrives without a motion stops the window's movement, so the window always ends on its newest Layout rectangle, and a hidden window is put back on its Layout rectangle while hidden. A window cannot be made smaller than its own minimum size, so a small `popin` start or `gnomed` starts at that minimum.

## Groups

A Group is one tile that holds several Clients. Only the shown member is in the Layout. The other members are hidden and keep their Workspace, so they are listed by `clients` with `hidden` true and their `grouped` list. They are recorded as hidden, shown again on a clean exit, and shown again after a crash, like any hidden Client. A Group is not rebuilt after a restart; its members come back as separate Clients.

`togglegroup` makes the focused Client a Group of one, and on a grouped Client ends the Group. Ending a Group leaves the tile to the last member, as the pinned release does, and gives every other member its own tile. A fullscreen Client leaves fullscreen first. Making a Group emits `togglegroup>>1,ADDRESS` and ending it emits `togglegroup>>0,ADDRESS` with the first member, one address only, as the pinned release's source does.

`changegroupactive` shows the next member, or the previous one with `b` or `prev`. A number picks the member at that position, counted from 1, and 0 or less picks the last. The shown member keeps the tile's place, size, floating and fullscreen state in every Layout. Focus moves to the newly shown member when the Group was focused. A window that is not grouped is an error, and so is a Group of one. `movegroupwindow` swaps the shown member with the next one, or the previous one with `b` or `prev`, in the Group order. `focuswindow` on a hidden member shows it first.

`moveintogroup` with `l`, `r`, `u`, `t`, `d` or `b` moves the focused Client into the Group of the Client in that direction. The moved Client becomes the shown member and is focused, and it emits `moveintogroup`. `moveintoorcreategroup` makes that neighbour a Group first. `moveoutofgroup` takes the focused Client, or the Client a window selector names, out of its Group into its own tile, next to the Group, and emits `moveoutofgroup`. `group:focus_removed_window` (default true) focuses the removed Client; false focuses the Group. `movewindoworgroup` moves into a Group in that direction, out of the Group when the neighbour is not grouped, and otherwise moves the Client like `movewindow`.

`lockgroups` locks every Group: no argument, `lock` or `lockgroups` lock, `toggle` toggles, anything else unlocks. It emits `lockgroups>>1` or `lockgroups>>0`. `lockactivegroup` locks the focused Group with `lock`, toggles with `toggle`, and unlocks otherwise. `denywindowfromgroup` stops other windows from joining the focused Group with `on`, toggles with `toggle`, and allows them otherwise. A locked Group refuses `moveintogroup`, `moveoutofgroup` and new windows. `binds:ignore_group_lock` (default false) lets the dispatchers pass a lock. `setignoregrouplock` does nothing, as in the pinned release. `binds:movefocus_cycles_groupfirst` (default false) makes `movefocus l` and `movefocus r` show the previous or next member until the first or last member is focused, and then leave the Group. A Group with nothing beside it on a single Monitor keeps cycling.

`group:auto_group` defaults to true, as in the pinned release's source. A window that opens while a Client of an unlocked Group is focused, on the same Workspace, joins that Group as its shown member. A dialog does not join, and a floating window does not join a tiled Group. `group:insert_after_current` (default true) puts a new member after the shown one; false puts it last. `group:group_on_movetoworkspace` (default false) puts a Client moved onto a Workspace whose only visible Client is an unlocked Group into that Group. Moving the shown member to another Workspace or Monitor takes the whole Group with it.

The `group` window rule takes the options `set`, `new`, `lock`, `invade`, `barred`, `deny`, `override`, `unset` and `always`, separated by spaces. `set` opens the window as a Group, and `lock` locks it. `barred` stops it from joining the focused Group, `deny` stops it from joining any Group, and `invade` lets it join a locked one. The last matching rule wins. `group` with no options is `set`, as the manual says.

`group:col.border_active`, `group:col.border_inactive`, `group:col.border_locked_active` and `group:col.border_locked_inactive` color the border of a grouped Client. The locked defaults are the ones in the pinned release's source, which swaps the manual's two values. `general:col.nogroup_border` and `general:col.nogroup_border_active` color a Client with a `group deny` rule. A gradient keeps its first color.

Winland draws the tab bar itself, in its own window just above the shown member in the z-order. A tiled Group's bar takes the top of the tile and the shown member is placed below it. A floating Group's bar sits above the window. From the bottom up the bar is `gaps_out`, the indicator (`indicator_height`), `indicator_gap`, the title row (`height`, when `render_titles` is on) and, with `keep_upper_gap`, another `gaps_out`. Tabs share the width with `gaps_in` between them. Sizes are logical pixels multiplied by the Monitor's scale. The tab of the focused Client uses `col.active` and `text_color`, and the others use `col.inactive` and `text_color_inactive`; a locked Group uses the `locked_` colors. A color's alpha is kept. `font_family` (default Segoe UI), `font_size`, `font_weight_active`, `font_weight_inactive`, `text_offset` and `text_padding` are honored. Titles are read from the windows and redrawn within a second of a change. `enabled` false or `disable_when_only` with one member draws no bar and takes no space. A fullscreen Client in mode 2 has no bar.

A click on a tab focuses that member, and a click between tabs focuses the shown member. A middle click closes the member when `middle_click_close` is on. The wheel focuses the next or previous member when `scrolling` is on.

## IPC pipe

A client has 5 seconds after it connects to send its request line, as in the pinned release. One that sends nothing in that time is disconnected. After the reply the Daemon waits up to 30 seconds for the client to close. Each client is served on its own and an idle `events` subscriber holds no thread, so a slow or silent client never keeps another, or a bind, waiting.
