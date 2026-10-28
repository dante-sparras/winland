# Elevation does not leak to launched programs or the IPC pipe

Programs started by `exec` and `exec-once` always run de-elevated, as the normal user. The IPC pipe accepts only the current user's SID, at any elevation, and `exec` works over it the same way. Nothing in the config can run code with Winland's rights, so the config is an ordinary file the user edits with any editor.

The default pipe name includes the Windows session id, so signed-in users have separate Daemons. `WINLAND_PIPE` can override it. The CLI and install commands verify the server process's session and user before trusting it, including on an overridden name. A process that claims the name first can prevent startup, but a server belonging to another user or session receives no commands or foreground rights.

The shell token used for launches is checked after duplication. An elevation type of Full is refused. Default is allowed so machines with UAC disabled still work. Config and `source` reads, default-config writes and taskbar marker operations impersonate that same shell token. A junction or file-link swap therefore has only the shell user's file access, and the config cannot read administrator-only files into warnings or `configerrors`.

An elevated Winland refuses `sendshortcut` when the destination process is elevated or its token cannot be checked. This applies to binds as well as the pipe, including a shortcut with no explicit destination. Otherwise an ordinary pipe client could type commands into an administrator terminal with Winland's rights. The foreground is checked again immediately before sending.

Winland is elevated by default (ADR 0004), so anything it starts would be elevated too, and anything that can write its config or its pipe could start programs as an administrator. An opt-in for elevated launches would make the config a door to administrator rights, and it is only safe when the config is writable by administrators alone. That is a protected folder and an elevated editor for a feature few need. A program that needs administrator rights asks for them with its own UAC prompt.
