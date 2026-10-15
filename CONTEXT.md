# Winland

Winland is the Windows 11 window manager that matches Hyprland's features and behavior.

## Language

**Window**:
A top-level OS window that Winland has not adopted.
_Avoid_: HWND, app

**Client**:
A Window that Winland manages: it places, sizes, focuses and closes it.
_Avoid_: Managed window, tile

**Managed**:
The state of a Window that has become a Client. A Window that no rule adopts stays unmanaged and is left alone.

**Gap**:
A Hyprland feature or behavior that Winland does not match, because Windows cannot support it or by choice. Each Gap is documented.
_Avoid_: Limitation, TODO

**Non-goal**:
A Hyprland feature that Winland will never have because it has no Windows meaning: XWayland, DRM and GPU options, Wayland protocol options, and the other Hypr ecosystem tools.
_Avoid_: Permanent gap

**Layout**:
The algorithm that places the Clients of a Workspace. Winland ships every Layout Hyprland has: floating, dwindle, master, monocle and scrolling.
_Avoid_: Tiling mode, arrangement

**Workspace**:
A set of Clients that Winland shows or hides together, owned by Winland and not by Windows virtual desktops.
_Avoid_: Virtual desktop

**Daemon**:
The `Winland.Daemon` process, built as `winland`. It is the one long-running, elevated process: it hosts the core, the Windows adapter, the Plugins and the IPC pipe, and a scheduled task starts it at logon.
_Avoid_: Service

**Plugin**:
A .NET assembly loaded into the Daemon that registers Dispatchers, config keywords and event handlers. It loads only when the SHA-256 of its file is approved. Layouts from a Plugin are reserved and refused for now.

**Plugin manager**:
The `winlandctl plugin install`, `update` and `remove` commands. They clone and build a plugin repository as the normal user in a staging folder, and an elevated step behind one UAC confirmation copies each assembly into the plugin directory and approves its hash.
_Avoid_: Package manager

**IPC pipe**:
The named pipe `winland` that the Daemon listens on. `winlandctl` and scripts send it commands and Dispatchers, and read the Event stream from it. It accepts only the current user.

**Event stream**:
The lines the Daemon writes to a client that sends `events` on the IPC pipe, one event per line in the form `name>>data`. Window, Workspace and Monitor events, config warnings and Submap changes are reported in it.

**Submap**:
A named set of binds that is active only while that Submap is selected. A `submap` line in the config opens one for the binds that follow, and the `submap` Dispatcher selects it. `reset` or an empty name selects the default binds. Each change emits a `submap` event.
**Monitor**, **Dispatcher**, **Special workspace**, **Group**:
Used with the meaning they have in Hyprland's manual, unless a Gap says otherwise.
