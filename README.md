# Winland

A keyboard-driven tiling window manager for Windows 11.

## Install

1. Download `Winland-win-x64.zip` from [Releases](https://github.com/dante-sparras/winland/releases) and unzip it.
2. Run `winland\winland.exe install` and approve the prompt.

Winland is now running and starts with Windows. The build is unsigned, so if SmartScreen stops it, choose **More info** › **Run anyway**.

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

### Apps and editing

| Keys | Does |
| --- | --- |
| Win+Enter | Open Windows Terminal |
| Win+Shift+Enter or Win+Shift+B | Open your browser |
| Win+Shift+F | Open File Explorer |
| Win+Shift+N | Open Notepad |
| Win+C / Win+V / Win+X / Win+A | Copy / paste / cut / select all |
| Win+Ctrl+V | Clipboard history |

Alt+Tab, Win+L, Win+E, Win+Space and Win+Shift+S still do what Windows does.

Tip: turn off **Snap windows** (Settings › System › Multitasking) so Windows doesn't fight Winland over the Win+Arrow keys.

## Important files

`%USERPROFILE%\.winland\winland.conf` is your config. Every hotkey above is in it, and you can change them there. Winland writes it the first time it starts, with comments, and reloads it whenever you save. Right-click the tray icon to open it.

## Uninstall

Remove Winland from Settings › Apps, or run `winland uninstall`. The `%USERPROFILE%\.winland` folder is kept, and `winland uninstall --purge` removes it.

## Credits

Winland began from [Hyprland](https://github.com/hyprwm/Hyprland), and its default binds come from [Omarchy](https://github.com/basecamp/omarchy). Every option is documented in [docs/behavior.md](docs/behavior.md).
