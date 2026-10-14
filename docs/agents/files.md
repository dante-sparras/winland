# Files Winland keeps

A person edits `binds.json` and `settings.json` in `%USERPROFILE%\.config\winland` ([../user/config.md](../user/config.md)). These are the files Winland writes for itself. Do not write `lifetime` or `hidden` by hand except where the workflow in `AGENTS.md` writes `replacing` before a swap. A bad word there shows windows that should stay hidden, or leaves them hidden.

One window manager runs. Its lock is `Local\Winland`, and a second start leaves quietly. `Winland.Watch.exe` is the window manager's watcher, from `%LOCALAPPDATA%\Winland\watch\w-<stamp>-<pid>`. On `running` it shows the windows in `hidden` and starts `Winland.Host.exe` again. On `replacing` it waits 30 seconds for `Local\Winland`. On `stopped` it shows those windows and leaves Winland down. Winland owns `hidden`.
