# Winland

Winland is the Windows 11 window manager that matches Hyprland's behavior: Tiling, Layout, Workspaces, and every Chord. It runs on a Host alone, or under Winshell and Winoma.

## Language

**Winland**:
The window manager product: Tiling, Layout, Workspaces, and every Chord. It runs on a Host without the other two products and draws no Bar. What it matches is Hyprland's behavior, not Hyprland's compositing.
_Avoid_: the tiler, the WM, Winoma (for this part)

**Host**:
A Windows 11 PC that runs Winland. Display layout, browser, and filesystem paths are settings of that Host.
_Avoid_: the Predator, this machine (as if it were the only one)

**Super**:
The Windows key, used as Omarchy Quattro's modifier.
_Avoid_: Win (in chord names), Meta

**Workspace**:
A numbered set of windows that Winland keeps, from 1 to 9. The numbers are one set for every monitor, and a Workspace belongs to one monitor. Each monitor has one Workspace in front: its windows are on the screen there, and Winland hides the windows of every Workspace that is in front nowhere. One Workspace in front has the keyboard, and Chords act on it. A Workspace exists while it holds a window or is in front, so the numbers in use may leave holes. A number Chord for one that does not exist makes just that one, on the monitor with the keyboard, and an empty Workspace stops existing once it is in front on no monitor. A number Chord for a Workspace on another monitor takes the keyboard there, and the Workspace stays on its monitor. The Bar always lists the first four, and any other that exists. The active window can be sent to a Workspace with or without following. Winland tiles the windows of each Workspace in front, on its monitor. `Super+Shift+Alt+Arrow` moves the Workspace with the keyboard onto the monitor in that direction. A Workspace is not a Windows virtual desktop, and Winland never makes, removes, or switches one.
_Avoid_: Winland-owned slot, monitor, virtual desktop

**Layout**:
Dwindle or scrolling, stored per Workspace and kept across a restart. `Super+L` toggles it. Dwindle is the default. A window is never drawn smaller than the size it will accept. When those sizes no longer fit, the windows pack into a grid, and they go back to dwindle when there is room. Scrolling lines windows up past the edge of the screen.
_Avoid_: lock, stack

**Tiling**:
Windows on a Workspace share the screen without overlapping, until one is floated or made fullscreen, and a tiled window that will not shrink below a size gets a tile at least that large, with another place when it keeps a larger size than its tile. A dialog, an owned window, and a fixed popup stay Free, and a Picture-in-picture player is shown on every Workspace. A keyboard focus leaves the pointer where it is.
_Avoid_: snapping, FancyZones

**Free**:
A window on a Workspace that does not take a tile: a dialog, an owned window, or a fixed popup. Winland lifts it above the tiles.
_Avoid_: float, dialog

**Scratchpad**:
Windows that are on no Workspace, hidden until shown over the Workspace with the keyboard. There is one, for every monitor: shown on another monitor, its Chord brings it to the one with the keyboard. `Super+S` and `Super+grave` show or hide them. `Super+Alt+S` and `Super+Shift+grave` move the focused window there. A scratchpad window moved to a Workspace while the scratchpad is shown takes a tile there. A shown scratchpad is hidden when another Workspace comes in front on its monitor. One scratchpad window is shown as a centered panel, and two or more share the full width.
_Avoid_: special workspace, a tenth Workspace

**Sticky**:
A floated window that is on no Workspace, so no Workspace switch hides it. `Super+O` toggles it.
_Avoid_: Picture-in-picture, pin

**Picture-in-picture**:
A player that is on no Workspace, titled “picture in picture”, that stays above other windows and that no Workspace switch hides. It is not tiled and it is not Free. `Super+T` gives it a tile: it is then kept above nothing, and is a window like any other until `Super+T` floats it again.
_Avoid_: overlay, a second tile

**Admission**:
Where a top-level window sits relative to Tiling. It takes a tile, stays Free, is Picture-in-picture, or is left alone.
_Avoid_: census, classification

**Chord**:
A key binding Winland handles. Navigation, launch, clipboard, and lock chords follow Omarchy Quattro unless an Exception or a Windows reserve says otherwise.
_Avoid_: hotkey (for Winland's own bindings), shortcut

**Chord command**:
What a Chord runs, such as `float` or `workspace.jump.3`. Its name in `binds.json` is the chord id. One may have no Chord, and two Chords may run the same one. A Plugin command is a plugin's own, and a CLI command is Quattro's word for the same work.
_Avoid_: command (alone, where a CLI command could be meant), shortcut

**Exception**:
A Chord Windows keeps. `Alt+Tab` and `Alt+Shift+Tab` stay the Windows switcher.
_Avoid_: override, reserved shortcut

**Caption**:
The window title bar. Close, minimize, and maximize are not offered. `Super+Q` and `Super+W` close the window. `Super+F` fills the screen. `Super+T` floats.
_Avoid_: taskbar button

**Spacing**:
Empty pixels between tiled windows on a Workspace. Off by default, so tiles sit flush. `Super+Shift+Backspace` toggles it. The size is a Host setting.
_Avoid_: Gap

**Square tile**:
One window drawn square on its Workspace. `Super+Ctrl+Backspace` toggles it.
_Avoid_: Spacing, Pseudotile

**Group**:
Windows that share one tile. `Super+G` toggles the Group on the active window. `Super+Alt+Arrow` pulls the focused window into the Group in that direction. `Super+Alt+Tab` cycles the Group. The tile keeps a strip free along its top, where a tab for each window is drawn, and the windows behind the one shown are hidden at the tile's size. A restart rebuilds a Group.
_Avoid_: Game Bar, stack panel

**Pseudotile**:
A tiled window drawn at a stored size, centered in its tile. `Super+P` toggles it. A resize chord changes that stored size.
_Avoid_: Free, float

**Glide**:
A tiled window travelling to its tile, over 379 milliseconds on Omarchy Quattro's curve. A window whose tile moved or changed size glides there, and so does a window pulled off its tile and let go. A tile that makes room for a window that opens sets off once that window has faded in. A tile that takes the room of a window that left takes it at once, and so does a window that has never been on a tile, and every window on a Workspace change (ADR 0081).
_Avoid_: animation, snap, return

**Fade**:
A window going from clear to solid as it opens, or from solid to clear as Winland closes it. Windows draws it on the window itself. A window that closes itself does not fade (ADR 0081).
_Avoid_: pop-in, transition

**Carry**:
Moving a window with the pointer: Super and the left button, pressed anywhere on the window. The window follows the pointer. Letting go drops a tile among the tiles where the pointer is, and leaves a floated or Sticky window where it is. A window let go on another monitor joins the Workspace in front there. A release that barely travelled leaves the tiles as they were.
_Avoid_: Super-drag, drag (for this gesture)

**Windows reserve**:
A Chord Windows will not release. `Ctrl+Alt+Del` is a Windows reserve, so Winland cannot bind "close every window" there the way Omarchy Quattro does.
_Avoid_: Exception (Exceptions are chosen; reserves are imposed)

**Layout function**:
`hdl`, `hds`, `hdlm`, or `hsl`, written in the PowerShell Managed block. `hdl` opens an editor, an agent, and a terminal. `hds` opens an editor, a diff watch, a terminal, and OpenCode. `hdlm` does `hdl` once per subdirectory. `hsl` fills a grid with the same command. Each refuses to run unless the pane is already inside Herdr. The diff watch is `hunk diff --watch`, and an empty role offers to install hunk the first time.
_Avoid_: tdl, the archive and worktree helpers

**Elevation**:
Windows asking before it runs something as administrator. Winland does not open a timed window where those prompts stop.
_Avoid_: passwordless sudo, a standing admin grant
