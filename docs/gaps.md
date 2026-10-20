# Gaps

These keywords are not honored. The config parser warns with the line number, skips the line, and still loads the config. A keyword that is not listed here and is not one Winland reads is skipped with the same warning; it is not a Gap until it is recorded here.

Honored behavior is in [behavior.md](behavior.md), the additions Winland makes to Hyprland are in [extensions.md](extensions.md), and key and scan codes are in [keys.md](keys.md). The terms Gap and Non-goal are defined in [CONTEXT.md](../CONTEXT.md).

## Keywords

This table has one row per keyword. A test checks it against the keywords the config parser skips, so every Gap keyword is a row here.

| keyword | reason |
| --- | --- |
| acrylic | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| allow_tearing | Tearing is a compositor feature with no Windows meaning. |
| animation:fadeLayers | Layer shell surfaces have no Windows meaning. |
| animation:fadeLayersIn | Layer shell surfaces have no Windows meaning. |
| animation:fadeLayersOut | Layer shell surfaces have no Windows meaning. |
| animation:fadePopups | Wayland popups have no Windows meaning. |
| animation:fadePopupsIn | Wayland popups have no Windows meaning. |
| animation:fadePopupsOut | Wayland popups have no Windows meaning. |
| animation:layers | Layer shell surfaces have no Windows meaning. |
| animation:layersIn | Layer shell surfaces have no Windows meaning. |
| animation:layersOut | Layer shell surfaces have no Windows meaning. |
| animation:monitorAdded | A monitor that is added zooms its picture and fades its wallpaper in. Windows draws that picture and that wallpaper, so neither can be animated. |
| aurora | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| binds:disable_keybind_grabbing | Applications cannot ask Windows to turn binds off, so there is nothing to refuse. |
| cursor:enable_hyprcursor | The cursor theme format has no Windows meaning. |
| cursor:min_refresh_rate | Cursor refresh rate has no Windows meaning. |
| cursor:no_break_fs_vrr | Pausing frames while the cursor moves has no Windows meaning. |
| cursor:no_hardware_cursors | Hardware cursors have no Windows meaning. |
| cursor:sync_gsettings_theme | Syncing the cursor theme with the desktop has no Windows meaning. |
| cursor:use_cpu_buffer | Hardware cursor buffers have no Windows meaning. |
| debug | Compositor debug options have no Windows meaning. The log and error bar options are honored. |
| decoration:blur:input_methods | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| decoration:blur:input_methods_ignorealpha | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| decoration:blur:new_optimizations | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| decoration:blur:popups | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| decoration:blur:popups_ignorealpha | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| decoration:blur:special | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| decoration:blur:variant | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| device:absolute_region_position | Tablets are not honored. |
| device:accel_profile | Windows has one pointer acceleration curve. Raw motion is in the device's own units, and taking over pointer movement to apply another curve is not reliable. |
| device:active_area_position | Tablets are not honored. |
| device:active_area_size | Tablets are not honored. |
| device:clickfinger_behavior | Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad. |
| device:disable_while_typing | Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad. |
| device:drag_3fg | Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad. |
| device:drag_lock | Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad. |
| device:enabled | Disabling a device node needs administrator rights. |
| device:flip_x | Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad. |
| device:flip_y | Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad. |
| device:kb_file | XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant. |
| device:kb_layout | Windows binds a keyboard layout to the foreground window, not to one keyboard. |
| device:kb_model | XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant. |
| device:kb_options | The keyboard hook applies the option before the keyboard is known. |
| device:kb_rules | XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant. |
| device:kb_variant | Windows binds a keyboard layout to the foreground window, not to one keyboard. |
| device:keybinds | Windows names the keyboard only after the key is delivered, and swallowing the key produces no raw input. |
| device:left_handed | SwapMouseButton swaps the buttons of every mouse, and the click is delivered before raw input names the mouse. |
| device:middle_button_emulation | Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad. |
| device:natural_scroll | natural_scroll only reverses the bind's key. The wheel is matched before the device is known, and a swallowed wheel produces no raw input. |
| device:numlock_by_default | Windows has one Num Lock state. |
| device:output | Windows cannot map touch to one display per device, and tablets are not honored. |
| device:region_position | Tablets are not honored. |
| device:region_size | Tablets are not honored. |
| device:relative_input | Tablets are not honored. |
| device:release_pressed_on_close | Virtual keyboards are not honored. |
| device:repeat_delay | Windows has one key repeat, and a repeat is not a raw packet from one keyboard. |
| device:repeat_rate | Windows has one key repeat, and a repeat is not a raw packet from one keyboard. |
| device:resolve_binds_by_sym | Binds that consume the key cannot tell which keyboard sent it. |
| device:scroll_button | Button scrolling is decided in the mouse hook before the device is known. |
| device:scroll_button_lock | Button scrolling is decided in the mouse hook before the device is known. |
| device:scroll_method | Button scrolling is decided in the mouse hook before the device is known. |
| device:scroll_points | One scroll curve is applied in the mouse hook before the device is known. |
| device:share_states | Virtual keyboards are not honored. |
| device:tap-and-drag | Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad. |
| device:tap-to-click | Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad. |
| device:tap_button_map | Precision touchpad settings apply to every touchpad, and the mouse hook does not name the pad. |
| device:transform | Windows cannot map touch to one display per device, and tablets are not honored. |
| dont_inhibit | Applications cannot inhibit binds, so this flag has nothing to bypass. |
| drops | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| ecosystem | Ecosystem tools have no Windows meaning. |
| experimental | Experimental Wayland protocol options have no Windows meaning. |
| fluid_jar | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| glass | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| haze | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| heat_shimmer | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| input-capture | The input capture portal has no Windows meaning. |
| input:kb_file | XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant. |
| input:kb_model | XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant. |
| input:kb_rules | XKB rules, models and keymap files have no Windows meaning. Layouts are chosen by kb_layout and kb_variant. |
| input:tablettool:eraser_button_mode | The eraser is a flag on the pen's pointer messages, not a mouse button the hook can retarget. Windows has no setting that turns it into a button, and changing it for every program needs UIAccess, which an unsigned build does not have. |
| input:tablettool:eraser_button_override | The eraser is a flag on the pen's pointer messages, not a mouse button the hook can retarget. Windows has no setting that turns it into a button, and changing it for every program needs UIAccess, which an unsigned build does not have. |
| input:tablettool:pressure_range_max | Windows gives pen pressure to the program under the pen on its pointer messages. A mouse hook sees the pen as a mouse, with no pressure, and there is no setting that sets a pressure range. Changing the pressure every program sees needs UIAccess, which an unsigned build does not have. |
| input:tablettool:pressure_range_min | Windows gives pen pressure to the program under the pen on its pointer messages. A mouse hook sees the pen as a mouse, with no pressure, and there is no setting that sets a pressure range. Changing the pressure every program sees needs UIAccess, which an unsigned build does not have. |
| input:virtualkeyboard:release_pressed_on_close | A virtual keyboard is a Wayland client that can release the keys it still holds when it closes. Windows has no such client, and an on-screen keyboard does not leave keys for another program to release. |
| input:virtualkeyboard:share_states | A virtual keyboard is a Wayland client. Windows has none: an on-screen keyboard sends keys into the one system keyboard state, which is already shared, and there is no separate state to leave unshared. |
| layerrule | Layer shell rules have no Windows meaning. |
| layerrulev2 | Layer shell rules have no Windows meaning. |
| locale | general:locale only selects the language of the program's own translated messages. Winland's messages are English and have no other language, and the option does not change how numbers or dates are written. |
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
| misc:name_vk_after_proc | A virtual keyboard is a Wayland client named after the process that creates it. Windows has no such device. An on-screen keyboard is not a separate device in the device list. |
| misc:render_unfocused_fps | Each application renders itself, so its frame rate has no Windows meaning. |
| misc:screencopy_force_8b | Screen copy buffers have no Windows meaning. |
| misc:session_lock_blur | The lock screen belongs to Windows. |
| misc:session_lock_xray | The lock screen belongs to Windows. |
| misc:splash_font_family | There is no splash text. |
| opengl | GPU rendering options have no Windows meaning. |
| permission | Permission rules have no Windows meaning. |
| quirks | Display buffer quirks have no Windows meaning. |
| render | GPU rendering options have no Windows meaning. |
| ripple | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| screen_shader | Screen shaders have no Windows meaning. |
| water | Only decoration:blur:enabled and, with decoration:blur:xray, the blur tuning options are honored. The DWM backdrop cannot be tuned. |
| xwayland | X11 compatibility has no Windows meaning. A window rule that matches on xwayland is not honored. |

## Kind and issue

Each keyword above is a `gap` or a `non-goal`. A non-goal has no Windows meaning at all. A gap is a feature Windows could support or Winland chose not to. [Possible](#possible) names every uncovered keyword and behavior Windows can support. The issue column links the open issue that tracks the keyword, or `-` when none does.

| kind | issue | keywords |
| --- | --- | --- |
| gap | - | acrylic, aurora, decoration:blur:variant, drops, fluid_jar, glass, haze, heat_shimmer, ripple, water |
| non-goal | - | allow_tearing, debug, opengl, quirks, render, screen_shader |
| non-goal | - | animation:monitorAdded |
| non-goal | - | animation:fadeLayers, animation:fadeLayersIn, animation:fadeLayersOut, animation:fadePopups, animation:fadePopupsIn, animation:fadePopupsOut, animation:layers, animation:layersIn, animation:layersOut |
| non-goal | - | binds:disable_keybind_grabbing |
| non-goal | - | cursor:enable_hyprcursor, cursor:min_refresh_rate, cursor:no_break_fs_vrr, cursor:no_hardware_cursors, cursor:sync_gsettings_theme, cursor:use_cpu_buffer |
| gap | - | decoration:blur:input_methods, decoration:blur:input_methods_ignorealpha, decoration:blur:new_optimizations, decoration:blur:popups, decoration:blur:popups_ignorealpha, decoration:blur:special |
| gap | - | device:absolute_region_position, device:active_area_position, device:active_area_size, device:region_position, device:region_size, device:relative_input |
| gap | - | device:accel_profile |
| gap | - | device:clickfinger_behavior, device:disable_while_typing, device:drag_3fg, device:drag_lock, device:flip_x, device:flip_y, device:middle_button_emulation, device:tap-and-drag, device:tap-to-click, device:tap_button_map |
| gap | - | device:enabled |
| non-goal | - | device:kb_file, device:kb_model, device:kb_rules |
| gap | - | device:kb_layout, device:kb_options, device:kb_variant, device:keybinds, device:resolve_binds_by_sym |
| gap | - | device:left_handed |
| gap | - | device:natural_scroll |
| gap | - | device:numlock_by_default |
| gap | - | device:output, device:transform |
| gap | - | device:release_pressed_on_close, device:share_states |
| gap | - | device:repeat_delay, device:repeat_rate |
| gap | - | device:scroll_button, device:scroll_button_lock, device:scroll_method, device:scroll_points |
| non-goal | - | dont_inhibit |
| non-goal | - | ecosystem, layerrule, layerrulev2, permission, xwayland |
| non-goal | - | experimental, input-capture |
| non-goal | - | input:kb_file, input:kb_model, input:kb_rules |
| gap | - | input:tablettool:eraser_button_mode, input:tablettool:eraser_button_override, input:tablettool:pressure_range_max, input:tablettool:pressure_range_min |
| non-goal | - | input:virtualkeyboard:release_pressed_on_close, input:virtualkeyboard:share_states, misc:name_vk_after_proc |
| non-goal | - | locale |
| non-goal | - | misc:allow_session_lock_restore, misc:anr_missed_pings, misc:background_color, misc:bell_sound, misc:col.splash, misc:disable_scale_notification, misc:disable_splash_rendering, misc:disable_watchdog_warning, misc:disable_xdg_env_checks, misc:enable_anr_dialog, misc:force_default_wallpaper, misc:layers_hog_keyboard_focus, misc:lockdead_screen_delay, misc:middle_click_paste, misc:render_unfocused_fps, misc:screencopy_force_8b, misc:session_lock_blur, misc:session_lock_xray, misc:splash_font_family |
| non-goal | - | misc:always_follow_on_dnd |

## Possible

These are the uncovered keywords and behaviors Windows can honor. A gap keyword in the tables above is in this list when Windows has the thing it names.

Drawing on windows Winland already owns: the blur of the windows behind a Client, which the blur options tune without `decoration:blur:xray`. Winland would capture those windows itself.

## Notes

`bitdepth, 10` turns wide color on, which composes the desktop above 8 bits. Windows has no switch for the bits of the link itself, so the output can stay at 8 bits, as the driver chooses, and a display without a wide color switch gets nothing. `bitdepth, 10` with `cm srgb` cannot be 10-bit: `srgb` turns HDR and wide color off. `sdrbrightness`, `sdrsaturation`, `sdr_min_luminance` and `sdr_max_luminance` are reported and do not change the picture. The SDR white level Windows publishes cannot be set, and Winland does not tonemap. `min_luminance`, `max_luminance` and `max_avg_luminance` replace the mastering luminance Winland stores for the display. Windows has no call that sets it, and the monitors reply does not carry those three.

An `animation:NAME` row is a node of the animation tree. An `animation` line that names it warns with that reason and is skipped. The other nodes are honored; see [behavior.md](behavior.md#animations). These parts of the honored nodes are Gaps:

- A window that closes by itself is gone before it can be drawn again, so `windowsOut` and `fadeOut` play only when Winland closes the window, with `killactive` or `closewindow`.

A row named by one part of a keyword covers every keyword with that part. `decoration:blur:glass:size` is the `glass` gap.

`no_vrr` and `misc:vrr` of 0 or 1 (`off` or `on`) write the account's graphics setting while Winland runs, described in [behavior.md](behavior.md#window-properties). A monitor rule's `vrr` still warns and is not honored: Windows has no switch for one display's adaptive sync. `misc:vrr` of 2 or 3 (`fullscreen`, `fullscreen_game`) warns and changes nothing, because those modes follow a fullscreen window and, for 3, a content type Windows does not report. The setting takes effect when the program next creates its swap chain, usually the next time it starts.

Two `misc` options turn off the reference compositor's logo and its check for its GUI utilities. Both are non-goals: Winland draws no logo and has no GUI utilities. Their names contain the name that [ADR 0002](adr/0002-hyprland-compatibility-without-its-name.md) keeps out of the code, so neither has a row above or an entry in the gap list in code. Each warns like any keyword Winland does not read.

A `gesture` line is honored for the `workspace`, `special`, `dispatcher`, `close`, `float`, `fullscreen`, `move`, `resize`, `cursorZoom` and `scrollMove` actions, and Winland's `luafunction`, described in [behavior.md](behavior.md#touchpad-swipe). `cursorZoom` zooms from 1 to 10, as `cursor:zoom_factor` does here, where the pinned release goes to 100, and a `live` pinch zooms around the point the camera follows rather than pinning the point under the cursor. The line is still kept, so it shadows a later gesture and `unset` removes it, as it would in the pinned release. Flag `p` (`gesturep`) warns and is ignored, for the same reason as bind flag `p`. A gesture sent with `keyword`, as `hl.gesture` called while Winland runs sends it, is added after the configured ones without the shadow warning, and `unset` there answers `Can't remove a non-existent gesture.` A Lua gesture's callback sees how far a pinch's fingers moved, but not its `scale` or `rotation`.

A touchpad swipe cannot be consumed. Windows reads the same fingers, so its own three- and four-finger gestures still run unless they are turned off (see [behavior.md](behavior.md#touchpad-swipe)). A window the swipe moves is moved itself, not drawn offset as the pinned release's compositor draws it, so a slow application can lag behind the fingers.

Bind flag `p` warns and is ignored. The bind itself still runs. Applications cannot inhibit binds, so there is nothing for that flag to bypass.

Blur is DWM's acrylic backdrop, so it shows only where DWM draws that backdrop: the title bar and any area a window leaves for it. What a window paints itself is not blurred, and a translucent Client does not blur what is behind its content. The exception is `decoration:blur:xray`: then a Client Winland makes translucent shows the blurred wallpaper under it, drawn by Winland with the blur `size`, `passes`, `noise`, `contrast`, `brightness`, `vibrancy`, `vibrancy_darkness` and `ignore_opacity` options (see [behavior.md](behavior.md#decoration)). Without `xray` those options have no DWM equivalent and change nothing, and with it the blur never includes a window, so a floating Client does not blur the windows behind it. A window that is translucent by its own program rather than by Winland has no backdrop of Winland's. A corner radius DWM does not draw is cut as a window region, and that edge is not antialiased. The drawn shadow surrounds the window and its border but not a Group's bar, and a `sharp` shadow is not drawn under the window, so a translucent Client does not show it through. The backdrop, shadow and DWM corner preference are not undone after a crash. A corner region Winland cut is removed on the next start.

Wobble and motion blur are drawn from a capture of the window, as [behavior.md](behavior.md#wobble-and-motion-blur) describes. These parts are Gaps:

- The border, shadow and glow drawn into the effect's frames hold still: a gradient does not turn with `borderangle`, `shadowangle` or `glowangle`, and a border color that `border` animates shows its new color at once, until the stand-in ends.
- A window Windows will not capture, such as one that excludes itself from capture, and a machine with no hardware graphics adapter, show the window without the effects.
- A window its program made layered cannot be made transparent for a stand-in. It is moved on every frame, without the effects.
- A Windows build that does not let a program capture a window without the capture border shows the window without the effects.
- A stand-in with effects starts after Windows delivers the window's first captured frame, about 20 ms for each window, so a motion that starts many windows at once starts late.

`device[name]:sensitivity`, `device[name]:scroll_factor` and `device[name]:rotation` are honored, as [behavior.md](behavior.md) describes. `device:tags` is kept for binds. The other `device` fields are the rows above: each one cannot be applied to one device on Windows. The keyboard of a key is known only after the key is delivered. Swallowing the key produces no raw input, so a bind cannot both name a device and consume the key. A consuming device bind, including one that lists a tag, is not registered, and the config warns once. Honoring it would need a kernel filter driver. A non-consuming device bind runs when the raw packet arrives on the input thread: the packet names the keyboard. A press runs from that packet. A repeat runs from the hook once the keyboard is known, and repeats that arrived before the packet, at most eight, run with it. A release uses the keyboard of the press. If the key is already up when the packet arrives, the press and the release both run from that packet. The packet for the key going up does not run the bind again. An unknown keyboard matches no device bind, including an exclusive one. A pointer button and a wheel turn have no device, so a device bind on them does not run. A key something else swallows has no packet, so its device bind does not run. A key `kb_options` moves is sent on as the key it becomes, and the raw packet names the original key, which was swallowed, so a device bind does not see it. A chord runs from the packet if the keys were held together when the triggering key went down. `auto_consuming` does not depend on the device: it swallows the key and injects the trigger key after the dispatcher returns. The injection is only the trigger key, a dispatcher that reports no error consumes the key even when it changes nothing, and an auto-consuming release bind cannot swallow the key because Windows must see the release of a press it saw. The other input keys stay Gaps.

`pass`, `sendshortcut` and `sendkeystate` to a window that is not focused bring that window to the foreground, send the key, and give the foreground back 30 ms later. Windows gives keys only to the foreground window. Each change of focus is reported. A mouse button goes to the window under the pointer, not to the named window.

`pass` from the pipe has no key to send and answers `pass: no key to pass`. The reference passes the last key pressed, and Winland sees only the keys of its binds.

`focusurgentorlast` reads the same history as the pinned release except for a window that opened and was never focused: the pinned release puts it at the oldest end when it opens, and Winland leaves it out until it is focused.

An application that asks for attention without flashing or taking the foreground is not urgent. `misc:focus_on_activate` set to true does not report the application as urgent before it takes focus.

`inhibitingIdle` reports only the `idle_inhibit` rule. An application that keeps the system awake by itself does so without Winland, and is not reported.

A window that asks for a position does not keep it: every new floating Client opens centered, or over its owner, unless `move` places it, as the pinned release treats a native window.

A `move` or `size` expression honors arithmetic, parentheses, the constants `_pi` and `_e`, and one-argument functions. Multi-argument functions such as `min` and `max`, comparison, logical and conditional operators, and the other built-in names are not honored: the rule warns and is skipped. A comma ends the effect in a window rule line, so a multi-argument function could not be written there anyway.

In a `move` expression, `window_w` and `window_h` are the size after a `size` rule. The pinned release reads the size the window asked for there, which a `size` rule does not change. Winland uses the size the window will have, so `move monitor_w-window_w 0` lines the window up with the right edge.

A scrolling resize with `scrolling:direction` up or down measures each delta along its own axis. The pinned release divides the column delta by the work area's width and the row delta by its height in every direction.

A pinned Client on a Monitor that is unplugged or disabled stays shown on the Monitor that takes its Workspaces. The pinned release moves it onto the Workspace the leaving Monitor shows next, which is a new, empty Workspace once the others have moved, and leaves that Workspace on the Monitor that went away, so the Client is not shown anywhere. Winland does not hide a window where nothing can reach it.

When a Monitor goes away or comes back and its Workspaces hide the focused Client, Winland focuses the Client the focused Monitor shows: the one focused last on that Workspace, or else its last shown Client. The pinned release moves only the Monitor focus and leaves the keyboard focus on the hidden window until the pointer or a dispatcher moves it. On Windows that would keep the foreground on a window out of sight.

A window rule that matches on `xwayland` is a Gap: the rule is skipped and the config warns. Other effects and match fields are skipped with a warning until a later change honors them.

## Lua config

A `winland.lua` keeps running while Winland runs (ADR 0013). The methods of the rule object `hl.layer_rule` returns are a non-goal: Windows has no layer surfaces, so `set_enabled` and `is_enabled` warn once and change nothing, and naming that rule again warns and changes nothing. These are gaps: a custom layout from `hl.layout.register` runs on the Lua thread after the change it answers, where the pinned release runs it inside that change (ADR 0013), so for a moment after a window opens every window takes a cell of an even grid, and after one closes or moves the others keep their boxes, until its boxes arrive. For the same reason a `layoutmsg` its `layout_msg` rejects, with false or a message, replies `ok` and the rejection goes to the log, as an error in its callbacks does, where the pinned release replies with it and shows the first error in the error bar. `require` looks a module name up in the config's folder only, where the pinned release goes on to Lua's own search path, because Winland's Lua reads no file the config's reader does not (ADR 0013).
`window.bell` and `screenshare.state` are non-goals, and `hl.on` accepts them without ever running their handlers. `window.bell` is a window asking to ring the system bell. Windows does not tell another program that a window did that, and a window that asks for attention is `window.urgent`. `screenshare.state` is a capture session the compositor hosts. Windows captures the screen itself and does not tell Winland when a share starts or stops. `layer.opened` and `layer.closed` are the same kind of non-goal: Windows has no layer surfaces, so their handlers never run. The events a plugin adds are accepted and never run. A click on a group bar tab runs `focuswindow`, so `window.active` gets 4, where the pinned release reports 1024 when that group was focused and 48 when it was not. `focuswindow` onto a workspace that was not shown always gets 4, where the pinned release reports the workspace change, 2048, or 2 across monitors, when that change already focused the window. `workspace` onto another monitor gets 2, where the pinned release reports 2048 from the monitor's change and 2 only when a later focus lands on another window. The pinned release runs a handler inside the change that raised its event and hands it objects of that moment. Winland runs it after the change, and the objects read Winland as it is then, so the window `window.close` gets reads nil. For the same reason `config.unload` runs once a reload has read the new config, where the pinned release runs it before. `window.open_early` runs just before `window.open`, after the window's rules, where the pinned release runs it as the window maps, before them.

The objects the `hl.get_*` queries return differ where Winland's replies have less: a window's `xwayland` is false and its `layout` has only `name`; a group's `locked` and `denied` are nil and its `current` is the member that is not hidden; each of a monitor's `available_modes` is `preferred = false`, and its `hardware_details` are `backend = "windows"`, `vrr_capable` false, and `hdr`, `chroma` and `bt2020` from the display's advanced color information and color primaries. A reply that leaves that object out stays false for them. `get_devices` lists the keyboards and mice Winland reports, whose fields are those of its `devices` reply. A keybind's `allow_input_capture` is false, and its `auto_consuming`, `device_inclusive` and `devices` are the bind's, its `arg` is the line dispatcher's argument, and disabling or removing it unbinds every bind of its keys. The urgent window, the last window, `focus_history_id` and the last workspace start empty when the config is loaded or reloaded. A monitor selector takes its id, name or `desc:`, but not the relative forms, such as `+1` or a direction. Within one call a reply is read once, so a change Winland makes by itself in that time, such as a window opening, shows only after a dispatch or in the next call. A comma in a monitor mode, position, scale or number, or in a workspace rule's flag or size, warns and is left out, since the line form splits at commas. A spring that a new motion takes over mid-way starts again from rest, where the pinned release keeps its velocity. These `hl.dsp` dispatchers have no line dispatcher and warn: `window.signal`, `global` and `release_input_capture`. `force_idle` is a non-goal. It would set how long clients have already been idle, and Winland reports idle to no listener. Windows does not let a program set that time. The cursor hide timer is not such a listener and is not advanced. A bind's `device` and `auto_consuming` are honored for keys, with the limits in the Notes.

`HL.Group:add` shows the added window. When the focused window is then a hidden member, the pinned release clears the keyboard focus: that member's input is blocked, and blocking the focused window unfocuses it, so no window has the keyboard focus. Winland moves the focus to the shown member. A member that is not shown is hidden with `SW_HIDE`, and that activates another window, so the hidden window does not keep the foreground. Putting the foreground back on the hidden window leaves it hidden, and the keystrokes go to a window the user cannot see. Windows also does not leave the foreground empty while another window is visible. A group object names the group of the window it was read from, so when that window leaves the group by another way, the object follows the window, not the group.

## Commands

Every command the pinned release's control socket answers is recognized. The ones below differ.

`descriptions` lists only the options Winland reads.

`getprop` reads and `setprop` sets every window property the pinned release has, described in [behavior.md](behavior.md#window-properties). `sync_fullscreen` is honored there. These are kept and reported and change nothing. Each is a Windows limit: the pinned release does it inside the compositor, and Windows has no supported way to do it to a window Winland does not own.

`allows_input` forces an X11 window to take input when that window asks not to be focused, an override-redirect window that does not want focus or a menu or dropdown. Winland does not adopt tool windows or tooltips, and no adopted window has a matching focus request. There is no Windows focus rule that does this for another process's window.

`nearest_neighbor` is the filter the compositor uses when it scales the window. Winland does not scale another application's pixels, and Windows exposes no per-window scaling filter.

`no_shortcuts_inhibit` refuses one window's request to inhibit compositor shortcuts. Windows has no such request. Winland reads keys in a low-level hook before the focused window sees them, and an application cannot turn binds off.

`force_rgbx` makes the compositor ignore the window's own per-pixel alpha and keep the colors. That is separate from Winland's opacity. Windows composites the window, and no call makes another process's per-pixel alpha opaque without replacing it with one alpha for the whole window.

`immediate` presents the window without waiting for the next frame, and only when tearing is allowed, the window is fullscreen, and it is the only thing on screen. Tearing is a compositor feature with no Windows meaning, and another process cannot set how an application presents its frames.

`render_unfocused` keeps telling the window to draw while it is not visible. Each application on Windows draws itself. There is no frame callback to send.

`no_screen_share` draws that window and its popups as black rectangles in a screen-share image while the window stays visible on the desktop. The documented display-affinity calls, `WDA_EXCLUDEFROMCAPTURE` and `WDA_MONITOR`, apply only to a window the calling process owns. Another process, elevated or not, is refused. There is no supported call that excludes a window Winland does not own from capture.

`no_auto_hdr` keeps the monitor's configured color mode when a fullscreen window's surface is HDR. The pinned release otherwise switches the monitor to HDR for that surface. Winland changes a display's color only from a monitor rule's `cm`, and it cannot read another window's color space, so it never switches the monitor for one window. The property has no switch to refuse. Windows Auto HDR is a different per-program tone map for SDR games, not that monitor switch.

A border color property is drawn with all its colors and its angle.

## Plugins

`plugin` loads an in-process plugin assembly and is honored, so it is not a Gap. A plugin may register dispatchers, config keywords and event handlers. Registering a Layout is refused until a later change.

`winlandctl plugin install`, `update`, `remove`, `enable`, `disable`, `reload` and `list --installed` are the plugin manager's `add`, `update`, `remove`, `enable`, `disable`, `reload` and `list`. See [plugins.md](plugins.md). These parts of the plugin manager are Gaps:

- `purge-cache` is not honored. There are no headers to cache, and `remove` deletes a repository's staging folder.
- Commit pins and the per-plugin minimum compositor version are not honored. The exact plugin API version replaces them, and header and ABI checks with it.
- `--no-shallow`, `--no-nix` and `--hl-url` are taken and do nothing. They change how the pinned plugin manager fetches and builds the compositor's headers, and Winland has no headers to fetch.
