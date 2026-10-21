# Winland is extended through its IPC pipe, not plugins

Status: Proposed (issue to be filed)

Winland loads no code into itself. A script or program in any language extends it from outside: it reads the Event stream and sends commands and Dispatchers over the IPC pipe, as the normal user. A config's `exec-once` can start it.

Winland runs elevated by default (ADR 0004), and .NET has no in-process sandbox, so code loaded into it would have administrator rights. Making that safe took an admin-only plugin folder, hash approvals, a UAC step, fault containment and time budgets, and building a plugin needed git and the .NET SDK on the user's machine. A script on the pipe can do what a plugin did, except arrange a custom Layout, and it runs with the user's rights, so it needs none of that. Hyprland plugins mostly draw inside the compositor, which Winland cannot do on Windows (ADR 0014).
