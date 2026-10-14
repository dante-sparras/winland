# Winland runs on top of Explorer and DWM

Winland is a background process. It watches top-level windows through Win32 event hooks, moves and resizes them, and registers global hotkeys. It does not replace `explorer.exe` or DWM, and it does not inject into other processes.

DWM always composes the screen on Windows, so Winland cannot be a compositor. Replacing the shell or hooking DWM would give more control, but it is fragile, breaks across Windows updates, and anti-cheat software rejects it. A shell replacement can be decided later without undoing this.
