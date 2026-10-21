# Workspaces are owned by Winland, and each Monitor has its own

Status: Proposed (issue to be filed)

A Workspace is Winland's own set of Clients. Switching hides the Clients of the old Workspace and shows those of the new one. Winland does not use Windows virtual desktops.

Each Monitor has its own Workspaces 1 to 10, and a number means that Workspace on the focused Monitor: Win+3 shows the third Workspace of the screen you are on, whichever screen that is. A Monitor is recognized by the make, model and serial its EDID reports, so a dock that renames its ports does not move Workspaces between screens. When a Monitor is unplugged, its Workspaces that hold Clients become Guests on the focused Monitor, numbered after its own, and they return under their old numbers when it comes back. Special workspaces belong to no Monitor and open on the focused one. There are no named Workspaces.

Moving windows between virtual desktops and switching them needs undocumented COM interfaces that change between Windows builds. Owning Workspaces gives Workspaces per Monitor, Special workspaces and silent moves directly. Clients on an inactive Workspace are hidden, which removes them from the taskbar and the window switcher. A minimized Client is hidden too, and showing its Workspace again leaves it minimized.

Numbers per Monitor follow Omarchy, which gives every monitor its own Workspaces 1 to 10 on top of Hyprland's global numbers with a layer of Lua and two state files. With one Monitor the two models behave alike; with several, the same key does the same thing on every screen, and nobody has to remember which numbers live where. Winland builds it into the core instead, which needs no state file and leaves one kind of numbered Workspace, so named Workspaces, which Omarchy leaves outside the scheme, go.
