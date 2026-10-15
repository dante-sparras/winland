# Workspaces are owned by Winland, not Windows virtual desktops

A Workspace is Winland's own set of Clients. Switching hides the Clients of the old Workspace and shows those of the new one. Winland does not use Windows virtual desktops.

Moving windows between virtual desktops and switching them needs undocumented COM interfaces that change between Windows builds. Owning Workspaces gives a Workspace per Monitor, named Workspaces, Special workspaces and silent moves directly. Clients on an inactive Workspace are hidden, which removes them from the window switcher. A minimized Client is hidden too, and showing its Workspace again leaves it minimized.
