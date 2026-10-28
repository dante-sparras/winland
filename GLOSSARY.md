# Winland

Winland is a keyboard-driven tiling window manager for Windows 11.

## Language

**Window**:
A top-level OS window that Winland has not adopted.
_Avoid_: HWND, app

**Client**:
A Window that Winland manages: it places, sizes, focuses and closes it.
_Avoid_: Managed window, tile

**Managed**:
The state of a Window that has become a Client. A Window that no rule adopts stays unmanaged and is left alone.

**Prop**:
A property of a Client, such as opacity, `no_focus` or `border_size`. A window rule sets it, and `setprop` overrides it.

**Layout**:
The algorithm that places the Clients of a Workspace. Winland ships floating, dwindle and scrolling.
_Avoid_: Tiling mode, arrangement

**Workspace**:
A numbered set of Clients on one Monitor that Winland shows or hides together. Each Monitor has its own Workspaces 1 to 10, so Workspace 3 means the third Workspace of the focused Monitor. Owned by Winland, not by Windows virtual desktops.
_Avoid_: Virtual desktop, named workspace

**Shown workspace**:
The Workspace a Monitor shows now, and the Special workspace it shows over it, if any. Each Monitor has exactly one Shown workspace, and a Workspace is shown on at most one Monitor.
_Avoid_: Active workspace, current workspace

**Guest**:
A Workspace shown on a Monitor other than its own, because its own Monitor was unplugged. It is numbered after that Monitor's own Workspaces, and returns under its old number when its Monitor comes back.

**Special workspace**:
A named Workspace that belongs to no Monitor. It opens over the focused Monitor's Workspace and closes again, keeping its Clients out of sight until it is next opened.
_Avoid_: Scratchpad

**Focused client**:
The Client that gets keyboard input. Each Workspace remembers the Client it last focused, which gets focus back when that Workspace is shown again.
_Avoid_: Active window

**Monitor**:
A display Windows reports, recognized by its make, model and serial. It shows one of its Workspaces at a time, and can show a Special workspace over it.
_Avoid_: Screen, output

**Group**:
A set of Clients that share one place in a Layout and show one at a time, switched like tabs.
_Avoid_: Tabbed container, stack

**Pinned**:
The state of a floating Client that shows on every Workspace of its Monitor. A child window that opens while its owner is pinned is pinned too.

**Dispatcher**:
A named action with an argument, such as `workspace 2`, that a bind, a gesture or the IPC pipe runs.
_Avoid_: Action

**Option**:
A named config setting with a category, such as `general:gaps_in`, that holds one value of one type. A later line that sets it replaces the earlier value, and with no such line it has its default.
_Avoid_: Setting, variable

**Daemon**:
The `Winland.Daemon` process, built as `winland`. It is the one long-running process, elevated by default: it hosts the window manager, the Windows adapter and the IPC pipe, and it starts at logon.
_Avoid_: Service

**IPC pipe**:
The named pipe that connects the Daemon to commands and scripts in the current user's Windows session. It carries commands, Dispatchers and the Event stream, and is the only way to extend Winland.
_Avoid_: Plugin API

**Event stream**:
The lines the Daemon writes to a client that sends `events` on the IPC pipe, one event per line in the form `name>>data`. Window, Workspace and Monitor events and config warnings are reported in it.

**Motion**:
One window's animation toward its Layout rectangle, or out of sight before it is hidden or closed. The window manager plans it from the animation tree, and the Windows adapter steps it. A Motion always ends on the newest Layout rectangle.
_Avoid_: Tween, transition
