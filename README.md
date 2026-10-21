# Winland

Winland is a tiling window manager for Windows 11. It runs on the Windows desktop and places the windows it manages into a Layout.

A Client is a window Winland manages: it places, sizes, focuses, and closes it. A window that no rule adopts is left alone. The taskbar, Start, and Search stay as Windows shows them.

A Workspace is a numbered set of Clients on one Monitor. Each Monitor has its own Workspaces 1 to 10, so Workspace 3 is the third Workspace of the focused Monitor. Winland owns these Workspaces. A Layout is the algorithm that places the Clients of a Workspace. The default Layout is dwindle. Winland also ships master, scrolling, monocle, and floating.

Winland began from [Hyprland](https://github.com/hyprwm/Hyprland). A Dispatcher is a named action, such as `workspace 2`, that a bind, a gesture, or the pipe runs. Winland's option and Dispatcher names come from Hyprland. The default binds come from [Omarchy](https://github.com/basecamp/omarchy).

Every option is in [docs/behavior.md](docs/behavior.md).

## Install

Download `Winland-win-x64.zip` from the [releases](https://github.com/dante-sparras/winland/releases) page. The zip has two folders and a `version.json` at its root. `version.json` records `commit`, `date` (UTC), and `version`. `winland/` is Winland, published self-contained for 64-bit Windows. `winlandctl/` is the control program, published the same way. Leave the files in each folder together. The release page also has `version.json` and `SHA256SUMS` for the zip and that file.

The build is unsigned. If SmartScreen stops `winland.exe`, choose **More info**, then **Run anyway**.

Unzip the archive. From the folder that contains `winland` and `winlandctl`:

```powershell
.\winland\winland.exe install
```

That asks for one UAC approval. Winland copies `winland` and `winlandctl` into `C:\Program Files\Winland`, adds that folder to the machine `PATH`, and adds a Start menu shortcut. Settings › Apps lists Winland. Winland starts, and from then on it starts at logon with no further prompt. Open a new terminal so `winland` and `winlandctl` are on `PATH`.

```powershell
.\winland\winland.exe install --user
```

That asks for no approval. Winland copies itself to `%LOCALAPPDATA%\Programs\Winland`, adds that folder to your `PATH`, and starts at logon from your Run key as the normal user. The Start menu shortcut and the Settings › Apps entry are yours. A Winland started from that copy never asks for administrator rights.

An install under Program Files removes a per-user install, so logon starts only one. A per-user install leaves a Program Files install in place.

## Administrator rights

Winland runs as administrator unless you installed it with `--user`. Windows blocks a program running as you from moving, resizing, or focusing a window that was started as administrator, and it delivers no keys while that window is focused. One approval at install keeps those windows in the Layout, and keeps the binds working while one of them is focused.

`--user` gives that up. An administrator window stays where Windows put it, and the binds do not run while it is focused.

Programs Winland starts run as you. To start one as administrator, ask Windows yourself. This starts Windows Terminal and shows that program's own UAC prompt:

```powershell
Start-Process wt -Verb RunAs
```

## First run and the config

On first start, when `%APPDATA%\winland\winland.conf` is missing and you did not pass `--config`, Winland writes the default config there and loads it. Edit that file and save it. Winland reads it again. `misc:disable_autoreload = true` leaves a save unread until `winlandctl reload`.

`winland --config <path>` reads that `.conf` file instead. The file has to exist already.

## Windows Snap and touchpad gestures

Win+arrow keys focus a Client. Windows also uses those keys to snap a window. Open Settings, then System, then Multitasking, and turn **Snap windows** off.

The default config switches Workspace when you swipe three fingers sideways on a precision touchpad. Windows sees that swipe too. Open Settings, then Bluetooth & devices, then Touchpad. Expand **Three-finger gestures** and set **Swipes** to **Nothing**. Expand **Four-finger gestures** and set **Swipes** to **Nothing**. A touchpad that is not a precision touchpad does not swipe in Winland.

## Default binds

Win is the Windows key. Alt+Tab, Win+L, Win+E, Win+Space, and Win+Shift+S stay with Windows: switching windows, locking the screen, File Explorer, the input language, and the snipping tool.

A Group is a set of Clients that share one place in a Layout and show one at a time. A Special workspace is a named Workspace that belongs to no Monitor. It opens over the focused Monitor's Workspace. The default binds use one named `scratchpad`. grave is the backtick key, left of 1 on a US keyboard.

| | Keys | What it does |
| --- | --- | --- |
| Close | Win+W, Win+Q | Close the focused Client |
| Split | Win+J | Swap the split direction |
| Own size | Win+P | Show the focused Client at its own size, centered in its tile |
| Float | Win+T | Float or tile the focused Client |
| Full screen | Win+F | Full screen |
| Maximize | Win+Alt+F | Maximize, which is fullscreen mode 1 |
| Pin | Win+O | Float and pin. Pressing again tiles it. A tiled Client cannot stay pinned |
| Layout | Win+K, Win+Alt+K | The next Layout, and the previous one |
| Focus | Win+arrows | Focus the Client in that direction |
| Swap | Win+Shift+arrows | Swap with the Client in that direction |
| To a Monitor | Win+Shift+Ctrl+arrows | Move the focused Client to the Monitor in that direction |
| Workspace to a Monitor | Win+Shift+Alt+arrows | Move this Monitor's Workspace to the Monitor in that direction |
| Workspaces | Win+1 through Win+9, Win+0 | Workspaces 1 to 10 of this Monitor. The 0 key is Workspace 10 |
| Move and follow | Win+Shift+1 through Win+Shift+0 | Move the focused Client to that Workspace and follow it |
| Move and stay | Win+Shift+Alt+1 through Win+Shift+Alt+0 | Move the focused Client there and stay on this Workspace |
| Scratchpad | Win+S, grave | Show or hide the Special workspace named scratchpad |
| To the scratchpad | Win+Alt+S, Win+Shift+grave | Move the focused Client onto that Special workspace and stay here |
| Next Workspace | Win+Tab, Win+Shift+Tab | The next and previous Workspace on this Monitor |
| Previous Workspace | Win+Ctrl+Tab | The Workspace this Monitor showed before this one |
| Next Monitor | Ctrl+Alt+Tab, Ctrl+Alt+Shift+Tab | The next and previous Monitor |
| Resize | Win+Minus, Win+Equal, and the same with Shift | Resize the focused Client by 100 pixels. Shift resizes up and down |
| Resize | the same keys with Alt, or with Ctrl | Alt is 25 pixels. Ctrl is 300. Shift still picks up and down |
| Wheel | Win+wheel | The next or previous Workspace on this Monitor |
| Drag | Win+left button, Win+right button | Drag a Client, or resize it |
| Group | Win+G | Put the focused Client in a Group, or take the Group apart |
| Leave the Group | Win+Alt+G | Take the focused Client out of its Group |
| Join a Group | Win+Alt+arrows | Join the Group in that direction |
| Group member | Win+Alt+Tab, Win+Alt+Shift+Tab | The next and previous member of the Group |
| Group member | Win+Ctrl+Left, Win+Ctrl+Right | The same on the arrow keys: previous, then next |
| Group member | Win+Alt+wheel | The same on the wheel |
| Group member | Win+Alt+1 through Win+Alt+5 | Show member 1 through 5 |
| Close all | Win+Ctrl+Alt+W | Ask every Client to close |
| Editing keys | Win+C, Win+V, Win+X, Win+A | The focused window receives Ctrl+C, Ctrl+V, Ctrl+X, and Ctrl+A |
| Clipboard history | Win+Ctrl+V | Send Win+V |
| Launch | Win+Enter | Windows Terminal (`wt`) |
| Launch | Win+Shift+Enter, Win+Shift+B | The default browser, opened at `https://` |
| Launch | Win+Shift+F | File Explorer |
| Launch | Win+Shift+N | Notepad |
| Touchpad | Three fingers sideways | Switch Workspace on this Monitor |

## Tray icon

Winland shows an icon in the notification area with the number of the Workspace the focused Monitor is showing. The tooltip names that Workspace. While a Special workspace is open, the icon keeps the number of the Workspace underneath.

A click or a right-click opens Reload, Edit config, Open log, and Quit. Reload reads the config again. Edit config opens the config file. Quit stops Winland and puts its windows back.

`general:tray_icon = false` removes the icon.

## Scripting

`winlandctl` sends commands to Winland on the named pipe `winland`. The pipe accepts only the current user.

Show Workspace 2 on this Monitor:

```powershell
winlandctl dispatch workspace 2
```

Print the event stream until you press Ctrl+C. Each line is one event, `name>>data`:

```powershell
winlandctl events
```

`winlandctl` with no arguments prints the other commands.

## Uninstall

```powershell
winland uninstall
```

That stops Winland and removes the logon startup, the Start menu shortcut, the Settings › Apps entry, the `PATH` entry, and the installed files. It keeps `%APPDATA%\winland`. Settings › Apps runs this command.

```powershell
winland uninstall --purge
```

That also removes the config in `%APPDATA%\winland`, and the logs and state under `%LOCALAPPDATA%\Winland`.

Uninstalling a per-user install asks for no approval. If Winland is installed under Program Files, uninstall asks for approval and then removes a per-user install as well.
