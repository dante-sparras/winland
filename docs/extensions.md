# Winland extensions

What Winland adds on top of Hyprland's config and window rules: syntax and behavior that Hyprland does not have. A keyword Winland does not honor is in [gaps.md](gaps.md), honored Hyprland behavior is in [behavior.md](behavior.md), and terms are defined in [CONTEXT.md](../CONTEXT.md).

`[elevated]` at the start of an `exec`, `exec-once`, `execr`, `execr-once`, or `exec-shutdown` command, or of an `exec` or `execr` dispatcher argument, launches that program with administrator rights. The same word inside a rule block does too, alone or with other rules. Without it the launch is de-elevated. A command received on the pipe never launches elevated, even when it starts with `[elevated]` or names it in the block, and `keyword` on the pipe refuses a bind that asks for it.

`[elevated]`, in an exec line, a bind or a workspace rule's `on-created-empty`, is honored only when the config file and every file it sources are writable by administrators alone: each is owned by `SYSTEM`, `Administrators` or `TrustedInstaller`, and no other principal may write it. Otherwise every launch runs de-elevated, and one config warning says which file a non-administrator can write. The default config, `%APPDATA%\winland\winland.conf`, is in a folder any program the user runs can write, so it is not trusted until protected. From an elevated shell, make Administrators the owner of the folder and of the files in it, and give the user read access only:

```
icacls "%APPDATA%\winland" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F "%USERNAME%":(OI)(CI)RX
icacls "%APPDATA%\winland\*" /reset /T
icacls "%APPDATA%\winland" /setowner *S-1-5-32-544 /T
```

After that the config is edited from an elevated editor. A file the user creates or replaces there again is owned by the user, and the config is untrusted until it is protected again.

`luafunction <id>` is Winland's dispatcher for a bind that calls a Lua function in `winland.lua`. The pinned release keeps such a function in the bind itself; Winland's bind lines name a dispatcher, so the function is kept in the config's Lua under a number and the bind names it. `winlandctl dispatch luafunction <id>` calls it too. A reload numbers the functions again.

`luafunction <id>` is also Winland's gesture action for an `hl.gesture` whose action is a Lua function or a table of them: `gesture = 3, left, luafunction, 1`. The core reports each phase of a swipe that takes the gesture, and the config's Lua runs the functions kept under that number, as [behavior.md](behavior.md) describes. In `winland.conf`, with no Lua config, it does nothing.

`resolveworkspace MONITOR SELECTOR` on the pipe answers the address of the workspace a selector names, read as a dispatcher on that monitor reads it, relative forms such as `e-1` too, when the workspace exists, and nothing otherwise. It makes no workspace. With `j/` the answer is a JSON string, or `null`. The Lua config's workspace queries use it, since the pinned release reads its selectors in place.

`groupadd MEMBER WINDOW [INDEX]` on the pipe puts WINDOW into the group MEMBER is in, at the 1-based INDEX, as `HL.Group:add` does, and `groupremove WINDOW` takes WINDOW out of its group, as `HL.Group:remove` does. `setworkspace MONITOR SELECTOR` shows a workspace on that monitor and `setspecialworkspace MONITOR [SELECTOR]` opens a special workspace there, or with no selector closes the one open, as `HL.Monitor:set_workspace` and `HL.Monitor:set_special_workspace` do, without focusing that monitor or moving the cursor. Each answers `ok` or the error. The pinned release does these only from its Lua objects, and Winland's Lua config reaches them through the pipe.

`lualayout = lua:NAME` is the config line `hl.layout.register` makes, which lets `general:layout` and a workspace rule select that custom layout. Written in `winland.conf`, it names a layout no Lua arranges, which tiles as an even grid. `layoutplace LAYOUT GENERATION [failed | WINDOW X Y W H ...]` on the pipe is how the Lua answers the core's request to lay a custom layout out (ADR 0013): the boxes it placed, or that it failed and the grid is used. An answer to a request a newer one replaced changes nothing.

`spring = NAME, STIFFNESS, DAMPING, MASS` is the line form of a spring `hl.curve`, which the pinned release has only in its Lua config. `winland.lua` writes it, and `animation = NODE, 1, SPEED, spring:NAME` puts a node on it. Each number must be above 0.5, and a line that breaks that or does not have three numbers warns and is skipped. See [behavior.md](behavior.md#animations).

`exe` is a `windowrulev2` match on the executable file name, including its extension. It uses the same regular expressions as `class` and `title`. `initialClass` and `initial_class` match the class the window had when it appeared, and `class` matches the class it has now. `initialTitle` and `initial_title` match the title the window had then, and `title` matches the title it has now.

`aumid` is a `windowrulev2` match on a packaged app's AppUserModelID. It uses the same regular expressions as `class`, `title` and `exe`. For a Store app hosted in a frame, `class` and `exe` are the hosted app's window class and executable file name, and `aumid` is that app's AppUserModelID.

`ignore` leaves the window unmanaged. `float`, `tile` and `workspace` force it to be adopted. `float` and `tile` also set whether the Client floats. `workspace` puts the Client on that Workspace; a trailing `silent` does not switch to it. `min_size` and `max_size` set the minimum and maximum track size, as two integers in pixels. `no_max_size` drops the maximum the window reported; a matching `max_size` still applies. `scrolling_width` sets the fraction of the Monitor used by the column a new tiled Client opens in. `pin` pins a floating Client so it stays shown on every Workspace of its Monitor. A trailing `0` turns that off. Pinning is ignored when the Client is tiled. The last matching rule wins when two rules disagree; rules that set different things combine.

`idleinhibit` is the same rule as `idle_inhibit`. `noinitialfocus` and `no_initial_focus` keep a new Client from taking focus. `nofocus` and `no_focus` refuse focus. `stayfocused` and `stay_focused` keep the focused Client focused when another window would take it. A trailing `0` turns that effect off. The last matching rule wins.

`setprop WINDOW PROP VALUE` on the pipe sets a window property as the `setprop` dispatcher does and answers `ok` or the error. The pinned release has only the dispatcher.

A last word `default` on `fullscreen`, or the word `default` after the action of `fullscreenstate`, asks for the default fullscreen rather than the Layout's. The pinned release has this only as `layout_aware = false` in its Lua dispatchers. See [behavior.md](behavior.md#fullscreen).

`misc:focus_on_activate` defaults to false. An application that takes the foreground without a recent click or key is sent back to the previous Client, and that application is reported as urgent when it is a Client. True accepts the change. A window that only flashes for attention, without taking the foreground, is reported as urgent too.

`winland install` registers the logon task that starts Winland elevated (ADR 0006). It refuses, and exits with code 1, when a non-administrator owns or can write the folder `winland.exe` is in or anything inside it, or owns a folder above it or may delete or rename what that folder holds or change its access list, because any program the user runs could then replace what the task starts, or rename the folder away and put another in its place. Making new folders above it is allowed, as `C:\` lets users do. Copy Winland to a folder only administrators can write, such as `C:\Program Files\Winland`, and run `winland install` from there.

A plugin installed by `winlandctl plugin install` or `update` is built as the normal user and approved through one UAC confirmation, which records the SHA-256 of the copied assembly. The host loads only approved assemblies from the protected plugin directory. A plugin repository describes itself in `winland-plugin.toml`, and a plugin with another API version is refused before anything is built. `winlandctl plugin list --installed` lists installed plugin repositories. See [plugins.md](plugins.md).

A floating Client that changes its own size within 2 seconds of opening is put back at the rectangle Winland opened it at, once. Electron applications restore their saved bounds just after the window is shown, which would undo the centering and the `move`, `size` and `center` rules. A move that keeps the size is left alone, and a later resize is the application's or the user's. A placement by `movewindowpixel`, `resizewindowpixel` or `centerwindow` ends that wait.

Animations follow the Windows setting **Animation effects** (Settings, Accessibility, Visual effects). When it is off, nothing animates, the same as `animations:enabled = false`.

When Winland starts, it adopts the windows that are already open, as if each had just appeared. A minimized window is left alone until it is restored. Hyprland has no such step, because every window it manages is created after it starts.
