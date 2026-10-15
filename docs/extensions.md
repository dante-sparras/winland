# Winland extensions

What Winland adds on top of Hyprland's config and window rules: syntax and behavior that Hyprland does not have. A keyword Winland does not honor is in [gaps.md](gaps.md), honored Hyprland behavior is in [behavior.md](behavior.md), and terms are defined in [CONTEXT.md](../CONTEXT.md).

`[elevated]` at the start of an `exec`, `exec-once`, `execr`, `execr-once`, or `exec-shutdown` command, or of an `exec` or `execr` dispatcher argument, launches that program with administrator rights. The same word inside a rule block does too, alone or with other rules. Without it the launch is de-elevated. A command received on the pipe never launches elevated, even when it starts with `[elevated]` or names it in the block.

`exe` is a `windowrulev2` match on the executable file name, including its extension. It uses the same regular expressions as `class` and `title`. `initialClass` and `initial_class` match the class, and `initialTitle` and `initial_title` match the title, because a rule is applied once when the window appears.

`aumid` is a `windowrulev2` match on a packaged app's AppUserModelID. It uses the same regular expressions as `class`, `title` and `exe`. For a Store app hosted in a frame, `class` and `exe` are the hosted app's window class and executable file name, and `aumid` is that app's AppUserModelID.

`ignore` leaves the window unmanaged. `float`, `tile` and `workspace` force it to be adopted. `float` and `tile` also set whether the Client floats. `workspace` puts the Client on that Workspace; a trailing `silent` does not switch to it. `min_size` and `max_size` set the minimum and maximum track size, as two integers in pixels. `no_max_size` drops the maximum the window reported; a matching `max_size` still applies. `scrolling_width` sets the fraction of the Monitor used by the column a new tiled Client opens in. `pin` pins a floating Client so it stays shown on every Workspace of its Monitor. A trailing `0` turns that off. Pinning is ignored when the Client is tiled. The last matching rule wins when two rules disagree; rules that set different things combine.

`idleinhibit` is the same rule as `idle_inhibit`. `noinitialfocus` and `no_initial_focus` keep a new Client from taking focus. `nofocus` and `no_focus` refuse focus. `stayfocused` and `stay_focused` keep the focused Client focused when another window would take it. A trailing `0` turns that effect off. The last matching rule wins.

`misc:focus_on_activate` defaults to false. An application that takes the foreground without a recent click or key is sent back to the previous Client, and that application is reported as urgent when it is a Client. True accepts the change. A window that only flashes for attention, without taking the foreground, is reported as urgent too.

A plugin installed by `winlandctl plugin install` or `update` is built as the normal user and approved through one UAC confirmation, which records the SHA-256 of the copied assembly. The host loads only approved assemblies from the protected plugin directory. A plugin repository describes itself in `winland-plugin.toml`, and a plugin with another API version is refused before anything is built. `winlandctl plugin list --installed` lists installed plugin repositories. See [plugins.md](plugins.md).

A floating Client that changes its own size within 2 seconds of opening is put back at the rectangle Winland opened it at, once. Electron applications restore their saved bounds just after the window is shown, which would undo the centering and the `move`, `size` and `center` rules. A move that keeps the size is left alone, and a later resize is the application's or the user's. A placement by `movewindowpixel`, `resizewindowpixel` or `centerwindow` ends that wait.
