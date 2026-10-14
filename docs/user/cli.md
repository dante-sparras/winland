# The `winland` command

`winland.exe` sits next to `Winland.Host.exe`. It asks the window manager the way `hyprctl` asks Hyprland. It does not need the shell to be up, and it does nothing the shell paints.

```powershell
winland workspaces
winland clients
winland activewindow
winland dispatch workspace.jump.1
winland clients -j
```

`-j` or `--json` may sit anywhere on the line. `winland`, `winland help`, and `winland --help` print the words and exit 0.

| Invocation | Exit | Prints |
| --- | --- | --- |
| `winland workspaces` | 0 | One line per Workspace: `workspace N`, then ` (front)` when that one is in front, then ` windows K` |
| `winland clients` | 0 | One line per window: handle, place, Workspace, title, separated by tabs |
| `winland activewindow` | 0 | The title of the window that has the keyboard, or its handle, or an empty line |
| `winland dispatch <command> [args]` | 0 | `ok` |
| the same words with `-j` | 0 | Those facts as JSON. A dispatch that ran prints `{"ok":true}` |
| Winland is not running | 1 | `Winland is not running` on stderr |
| a dispatch Winland refused | 1 | The sentence on stderr, or `{"ok":false,"error":"..."}` with `-j` |

`winland dispatch workspace.jump.3` moves to Workspace 3. A command the shell runs, such as opening a panel, is refused with `the shell runs that`.
