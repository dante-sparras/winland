# Winland

A keyboard-driven tiling window manager for Windows 11.

## Install

Open PowerShell and run:

```powershell
irm https://raw.githubusercontent.com/dante-sparras/winland/main/scripts/install.ps1 | iex
```

To install the dev release, built from `main` after every change, run:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/dante-sparras/winland/main/scripts/install.ps1))) -Channel dev
```

To install by hand, download `Winland-win-x64.zip` from [Releases](https://github.com/dante-sparras/winland/releases), unzip it and run `winland\winland.exe install`.

## Hotkeys

### Windows

| Keys | Does |
| --- | --- |
| Win+Q or Win+W | Close the window |
| Win+Ctrl+Alt+W | Close all windows |
| Win+Arrows | Move focus |
| Win+Shift+Arrows | Swap the window with its neighbor |
| Win+T | Float or tile the window |
| Win+F | Fullscreen |
| Win+Alt+F | Maximize |
| Win+O | Pop the window out (float and pin it) |
| Win+P | Show the window at its own size in its tile |
| Win+J | Flip the split direction |
| Win+K / Win+Alt+K | Next / previous layout |
| Win+Minus / Win+Equal | Make the window narrower / wider |
| Win+Shift+Minus / Win+Shift+Equal | Make the window shorter / taller |
| Add Alt or Ctrl to a resize | Resize in small or big steps |
| Win+left drag | Move a window with the mouse |
| Win+right drag | Resize a window with the mouse |

### Workspaces

| Keys | Does |
| --- | --- |
| Win+1 … Win+0 | Switch to workspace 1 to 10 |
| Win+Shift+1 … Win+Shift+0 | Move the window to a workspace and follow it |
| Win+Shift+Alt+1 … Win+Shift+Alt+0 | Move the window to a workspace and stay |
| Win+Tab / Win+Shift+Tab | Next / previous workspace |
| Win+scroll wheel | Next / previous workspace |
| Win+Ctrl+Tab | Back to the last workspace |
| Win+S or Win+\` | Show or hide the scratchpad |
| Win+Alt+S or Win+Shift+\` | Move the window to the scratchpad |
| Three-finger swipe sideways | Next / previous workspace |

### Monitors

| Keys | Does |
| --- | --- |
| Ctrl+Alt+Tab / Ctrl+Alt+Shift+Tab | Focus the next / previous monitor |
| Win+Shift+Ctrl+Arrows | Move the window to another monitor |
| Win+Shift+Alt+Arrows | Move the whole workspace to another monitor |

### Groups (tabbed windows)

| Keys | Does |
| --- | --- |
| Win+G | Make the window a group, or break the group up |
| Win+Alt+Arrows | Add the window to the group next to it |
| Win+Alt+G | Take the window out of its group |
| Win+Alt+Tab / Win+Alt+Shift+Tab | Next / previous tab |
| Win+Ctrl+Right / Win+Ctrl+Left | Next / previous tab |
| Win+Alt+scroll wheel | Next / previous tab |
| Win+Alt+1 … Win+Alt+5 | Go to tab 1 to 5 |

### Apps

| Keys | Does |
| --- | --- |
| Win+Enter | Open Windows Terminal |
| Win+Shift+Enter or Win+Shift+B | Open your browser |
| Win+Shift+F | Open File Explorer |
| Win+Shift+N | Open Notepad |

## Stop and undo

- To quit, right-click the Winland icon in the notification area, which Windows may keep behind the `^` arrow, and choose Quit. Or run `winland dispatch exit`. Either puts every window back.
- To remove Winland, run `winland uninstall`. It also turns Snap and the touchpad swipes back on, if the install turned them off.

## Important files

Everything Winland keeps for you is in `%USERPROFILE%\.winland`.

| File | What it is |
| --- | --- |
| `winland.conf` | Your config, with every hotkey above. Winland writes it the first time it starts, with comments, and reloads it whenever you save. Right-click the tray icon to open it |
| `winland.log` | What Winland did and any errors. Only there when `debug:disable_logs = false` and Winland is not running as administrator, which it does by default. Right-click the tray icon to open it |
| `state.json` | The windows Winland hid and the workspace of each, so a crash brings them back |
| `windows-settings.json` | The Windows settings the installer turned off, so uninstall can turn them back on |
| `taskbar.marker` | Only there while `general:hide_taskbar` hides the taskbar, so a crash still brings it back |

## CLI

| Command | Does |
| --- | --- |
| `winland` | Start Winland |
| `winland --config <file>` | Start Winland with that `.conf` file in place of your config |
| `winland install` | Install for this PC, running as administrator. Asks for one approval |
| `winland install --user` | Install for you only, without administrator rights |
| `winland install --no-logon` | Install without starting Winland at logon. Works with `--user` |
| `winland install --no-start` | Install and leave Winland stopped. Works with `--user` |
| `winland update` | Update to the newest release |
| `winland update --channel stable` | Follow the stable channel and remember the choice |
| `winland update --channel dev` | Follow the dev channel and remember the choice |
| `winland uninstall` | Remove Winland, turn back on any Windows setting the installer turned off, and keep the `.winland` folder |
| `winland uninstall --purge` | Remove Winland and the `.winland` folder |
| `winland tweaks --off` | Turn off Windows Snap, and remember how it was |
| `winland tweaks --off --touchpad` | Also turn off the three- and four-finger touchpad swipes |

These talk to the running Winland.

| Command | Does |
| --- | --- |
| `winland dispatch <name> [args]` | Run a Dispatcher, such as `winland dispatch workspace 2` |
| `winland reload` | Read the config again |
| `winland keyword <name> <value>` | Change one option until the next reload |
| `winland getoption <name>` | Show an option's value |
| `winland clients` | List the windows Winland manages |
| `winland activewindow` | Show the focused window |
| `winland workspaces` | List the workspaces |
| `winland activeworkspace` | Show the focused workspace |
| `winland monitors [all]` | List the monitors. `all` includes disabled ones |
| `winland binds` | List the hotkeys, one config line each |
| `winland configerrors` | List the config's warnings, each with its file and line |
| `winland getprop <window> <property>` | Show a window property |
| `winland setprop <window> <property> <value>` | Set a window property |
| `winland kill` | Click a window to end its program. Escape cancels |
| `winland version` | Show the version |
| `winland events` | Print every event as it happens, one per line |
| `winland --batch "<command>; <command>"` | Run several commands in one go |

| Flag | Does |
| --- | --- |
| `-j` | Answer in JSON |

## Credits

Winland began from [Hyprland](https://github.com/hyprwm/Hyprland), and its default binds come from [Omarchy](https://github.com/basecamp/omarchy). Every option is documented in [docs/behavior.md](docs/behavior.md).

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening an issue or PR. The [development runbook](docs/development.md) covers prerequisites, first checkout, and commands.

## License

[MIT](LICENSE)
