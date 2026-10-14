# The keyboard hook runs in Winland

Every key on the machine waits on the keyboard hook. The hook runs in `Winland.Host.exe`, with the binds, tiling, and the hidden record. It does not run in the shell, so a stall or a crash in WinUI does not take it down, and the shell can be down while the window manager still has the keys.

What the hook needs from the shell is pushed on the request pipe and cached: whether the capture picker is open and which keys it wants, whether a conditional bind such as Dictation's Escape is active, whether the session is locked, and the bar's edge, thickness, and windows. The hook callback reads that cache. It does not wait on a pipe round-trip. A global bind's key-down and key-up are published on the event pipe, and the shell runs them.

Winland reads `.config\winland\binds.json`. The shell does not install the hook and does not load the binds for it.

`keyboardHookProcess` and `Winshell.Host.exe --keys` remain in the tree. The Winoma session reads `--keys`. The desktop does not start that process. Hover focus is decided in Winland, on the window manager's thread, from the pointer the hook samples. The shell is not asked.
