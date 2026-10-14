# Workspaces are owned by Winland, not Windows virtual desktops

A Workspace is Winland's own set of Clients. Switching hides the Clients of the old Workspace and shows those of the new one. Winland does not use Windows virtual desktops.

Moving windows between virtual desktops and switching them needs undocumented COM interfaces that change between Windows builds. Owning Workspaces also gives Hyprland's semantics directly: a Workspace per Monitor, named Workspaces, Special workspaces and silent moves. The cost is that hidden Clients can still show in Alt-Tab and the taskbar, unless Winland cloaks them. Where it can't, that is a Gap.
