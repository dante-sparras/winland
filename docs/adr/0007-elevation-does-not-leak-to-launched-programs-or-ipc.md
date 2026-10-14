# Elevation does not leak to launched programs or the IPC pipe

Programs started by `exec` and `exec-once` run de-elevated, as the normal user. A launch is elevated only when the config file says so. The IPC pipe accepts only the current user's SID, at any elevation. `exec` works over the pipe but never elevated.

Winland is elevated (ADR 0004), so anything it starts would be elevated too, and anything that can write to its pipe could start programs as an administrator. Only the config file, writable only from an elevated session, may request an elevated launch.
