# Elevation does not leak to launched programs or the IPC pipe

Status: Proposed (issue to be filed)

Programs started by `exec` and `exec-once` always run de-elevated, as the normal user. The IPC pipe accepts only the current user's SID, at any elevation, and `exec` works over it the same way. Nothing in the config can run code with Winland's rights, so the config is an ordinary file the user edits with any editor.

Winland is elevated by default (ADR 0004), so anything it starts would be elevated too, and anything that can write its config or its pipe could start programs as an administrator. An opt-in for elevated launches would make the config a door to administrator rights, and it is only safe when the config is writable by administrators alone. That is a protected folder and an elevated editor for a feature few need. A program that needs administrator rights asks for them with its own UAC prompt.
