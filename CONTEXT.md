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

**Monitor**, **Dispatcher**, **Special workspace**, **Group**:
Used with the meaning they have in Hyprland's manual, unless a Gap says otherwise.
