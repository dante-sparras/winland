# Files Winland keeps

A person edits `binds.json` and `settings.json` in `%USERPROFILE%\.config\winland` ([../user/config.md](../user/config.md)). These are the files Winland writes for itself. Do not write `lifetime` or `hidden` by hand except where the workflow in `AGENTS.md` writes `replacing` before a swap. A bad word there shows windows that should stay hidden, or leaves them hidden.

| File | Holds |
| --- | --- |
| `hidden` | Each window Winland has hidden, and how (`cloak` or `hide`). Winland writes it and reads it. The shell reads it only when it starts and no window manager is running. Absent while none is hidden. `hidden.new` is the write beside it |
| `lifetime` | One word Winland writes and `Winland.Watch` reads: `running` while the window manager is up, `stopped` after a Stop or while the session is ending, `replacing` while one build takes another's place. The shell writes the same word here on a stop or a replace, and does not pass its own word across (ADR 0059, ADR 0067, ADR 0068) |
| `winland-restarts` | The times `Winland.Watch` started the window manager again. Three inside a minute stop that watcher. A shell crash does not spend this budget |
| `winland.log` | What the window manager did, in the same shape as `winoma.log`. `ready` on a Winland line means the hook is in and the window manager is up. The previous chunk is `winland.log.1` |

One window manager runs. Its lock is `Local\Winland`, and a second start leaves quietly. `Winland.Watch.exe` is the window manager's watcher, from `%LOCALAPPDATA%\Winland\watch\w-<stamp>-<pid>`. On `running` it shows the windows in `hidden` and starts `Winland.Host.exe` again. On `replacing` it waits 30 seconds for `Local\Winland`. On `stopped` it shows those windows and leaves Winland down. Winland owns `hidden`.
