# Each monitor has a Workspace in front

Each monitor has one Workspace in front, and one monitor has the keyboard. This is Hyprland's model, but for the pointer warp and for where `Super+N` puts a hidden Workspace. The numbers are one set, 1 to 9, across every monitor. Each Workspace belongs to one monitor, and its windows are tiled there. With one monitor every Chord does what it did before there was a second.

`WorkspaceTravel` keeps the Workspace in front on each monitor and the monitor each Workspace belongs to. `WorkspaceBook` keeps which Workspaces are in front and which one has the keyboard: the Workspace with the keyboard is the one the tiling Chords act on and the one `Super+Tab` counts from. Each Bar marks the Workspace in front on its own monitor (ADR 0080). `ShellController` shows the windows of every Workspace in front and hides the windows of every other (ADR 0003).

## What each Chord does across monitors

- **`Super+N`.** In front on the monitor with the keyboard, nothing. In front on another monitor, the keyboard goes there and nothing is shown or hidden. Hidden, on whichever monitor, it comes in front on the monitor with the keyboard, and is that monitor's own from then on: its windows are tiled there (`WorkspaceTravel.Go`). The Workspace that was in front there is hidden. A number that does not exist is made on the monitor with the keyboard. Hyprland leaves a hidden Workspace on its own monitor and sends the keyboard there. Winoma departs from it because Dante asked on 2026-10-11: a number that is not on the glass should appear where he is.
- **`Super+Shift+N`.** The window moves to N, and then N is reached as `Super+N` reaches it: where it is in front, or on the monitor with the keyboard when it was hidden. The keyboard follows the window.
- **`Super+Shift+Alt+N`.** The window moves and the keyboard stays. When N is in front on another monitor the window is drawn there. Otherwise it is hidden.
- **`Super+Tab` and `Super+Shift+Tab`.** Every Workspace that exists, on every monitor, in number order, wrapping. Each step is a `Super+N`, so a step to a hidden Workspace brings it to the monitor with the keyboard.
- **`Super+Ctrl+Tab`.** The Workspace that had the keyboard before, reached as `Super+N` reaches it. It is one memory for the desk, not one per monitor.
- **`Super+Shift+Alt+Arrow`.** The Workspace with the keyboard moves to the monitor that way and is in front there, with the keyboard. The Workspace that was in front there is hidden. The monitor that was left shows the lowest Workspace of its own that holds a window, or a new one with the lowest free number.
- **`Ctrl+Alt+Tab` and `Ctrl+Alt+Shift+Tab`.** The keyboard goes to the Workspace in front on the next monitor, or the previous, in the order the Host lists them, wrapping.
- **`Super+Arrow`.** Past the edge of the monitor it reaches the tile nearest across the edge on the monitor that way, and the keyboard goes there. With no window there the keyboard goes to that monitor all the same.

A Workspace exists while it holds a window or is in front on a monitor. Every monitor always has one in front, so a monitor with none takes the lowest free number. An empty Workspace stops existing once it is in front on no monitor.

## The keyboard

The monitor with the keyboard is the one whose Workspace has it. Six things move it besides a Chord.

- **The pointer moves onto a tile on another monitor.** Hover focus gives the tile the keyboard, and its Workspace is the one with the keyboard at once (`ShellController.Hover`). Only a pointer that moved does this (ADR 0034). After a Chord took the keyboard to another monitor, a pointer left behind takes nothing back until the mouse moves, and then it does, also onto the window the keyboard was left on.
- **The pointer moves onto the bare desktop of another monitor** (`ShellController.HoverDesktop`). The keyboard goes to the Workspace in front there as a Chord sends it: to its focused window, or to no window when it is empty. So a Chord pressed after the pointer went to an empty monitor acts on that monitor. Dante asked for this on 2026-10-12: with no window there to hover, the Chords kept acting on the monitor he had left. A held button and an open Panel keep the keyboard where it is, and so does a pointer over a Bar or any other window that is no tile.
- **The left button goes down on another monitor** (`ShellController.Press`). On a tile, its Workspace has the keyboard, and Windows gives the pressed window the keyboard itself. On the bare desktop, the keyboard goes to the Workspace in front there as a Chord sends it: to its focused window, or to no window when it is empty. A press a Carry took, a press on a size button, and a press while a Panel is open move nothing.
- **A window on another monitor takes the keyboard.** A click, or the window's own program, gave it the keyboard, and the window is on the glass already. Its Workspace is then the one with the keyboard (`ShellController.FollowShown`). Nothing is shown or hidden. The desktop looks when Windows reports the keyboard moved, and not only on the next turn of its loop.
- **A dialog takes the keyboard.** A window another window owns is on its owner's Workspace (`ShellController.WorkspaceOf`): Windows draws it above its owner and cloaks it with its owner. `winoma desktop` lists it there, the keyboard on it is on its owner's monitor, and its owner is the tile the Chords act on. A Chord that takes the keyboard off a dialog to another monitor is not undone by the dialog.
- **A window closes and Windows hands the keyboard on.** Windows gives it to the next window in its own order, which can be on the other monitor. Nobody asked for that monitor, so the keyboard goes back to the Workspace that had it.
- **A Chord sends the keyboard to a monitor and no window takes it there.** The Workspace is empty, or Windows refused the focus. The window that had the keyboard is still on the glass on the monitor that was left, so nothing hides it. The keyboard is dropped, and while Super is held it waits (ADR 0020). Until it has left that window, the window is not taken to have asked for the keyboard, so the Chord is not undone.

The Workspace-changed Hook fires whenever the Workspace with the keyboard changes, a change of monitor included. It does not fire when a Workspace moves to another monitor with the keyboard on it.

**The pointer never moves.** Hyprland warps the pointer to what takes focus. Winoma does not: keyboard focus never moves the pointer (ADR 0034). This is a decision, not a Gap.

## What is one for the whole desk

- **The Scratchpad.** There is one. It is shown over the Workspace with the keyboard, on that Workspace's monitor. Its Chord while it is shown on another monitor takes it from there. It hides when another Workspace comes in front on the monitor it is shown on, and stays when the keyboard only goes to another monitor (ADR 0021).
- **A Sticky window and a Picture-in-picture player** are on no Workspace and stay where they are.
- **A window that opens** joins the Workspace with the keyboard. One whose program Winoma started joins the Workspace that had the keyboard at the start, and the keyboard stays where it is (ADR 0038).

A window a Carry lets go on another monitor joins the Workspace in front there, among the tiles under the pointer, and the keyboard goes with it. A floated window stays floated.

A fullscreen window makes only its own monitor's Bar yield (ADR 0080).

While the Bar is hidden, a monitor is tiled from edge to edge, so a lone tile covers it. That tile is a tile: it loses its caption buttons like any other, and it is no fullscreen window. A window with its buttons is taken for fullscreen in its own way, and keeps its frame, only on a monitor where a tile cannot cover the monitor.

## Monitors that come and go

A monitor that goes away hands its Workspaces to the monitor the first Bar is on (ADR 0080). They are Workspaces of that monitor meanwhile, hidden until one is asked for, and each remembers the monitor it came from. When that monitor is back they return, and the one that was in front there is in front again. A keyboard that was on the removed monitor goes to the Workspace in front on the monitor the first Bar is on. The shell finds a change in the monitors at each arrange (`WorkspaceTravel.Settle`). The tiles on a monitor that stayed do not move when another comes or goes, so nothing else would ask for that arrange: the shell says the monitors are not the ones it last arranged (`ShellController.MonitorsChanged`), and the desktop arranges. That is about a tenth of a second after Windows reports the change. When no report comes, the loop finds it on a turn, once the monitor list it keeps is five seconds old.

A monitor is known by its rectangle, never by its place in the Host's list, which changes when a monitor is added before it.

## Nothing on disk

No file says which monitor a Workspace is on or which Workspace is in front. A desktop that starts reads both from the windows (ADR 0078):

- A Workspace is on the monitor that holds the most of its windows. A hidden window has not moved, so it counts.
- The Workspace of the window with the keyboard is in front on its monitor. On each other monitor it is the Workspace with the most windows on the glass there, and the lowest number among equals.
- A monitor that shows no window takes the lowest free number. An empty Workspace in front is therefore not kept across a restart, and no hidden window is shown by a start.

## One placement names every tile

The Host is given every tile on the glass in one placement (`ITilePlacement`), and the last placement is the one it keeps. So each Workspace in front is placed with the tiles of the others beside its own, and the one with the keyboard is placed last. An arrange that places is one placement for each monitor, and one with one monitor, as before. Each placement fits only its own Workspace's tiles: a window drawn beside them is not measured again (`TileFit`). An arrange that finds every monitor as it was places nothing.

## Considered options

- **One Workspace in front for every monitor.** Winoma did this while a Workspace was a Windows virtual desktop. A person with two monitors could not read on one and work on the other.
- **`Super+N` always brings the Workspace to the monitor the person is on**, as xmonad does, also when it is in front on another monitor. The two monitors would then trade Workspaces at a key, and the one the person was reading on the other monitor would be replaced.
- **`Super+N` leaves a hidden Workspace on its own monitor**, as Hyprland and Omarchy Quattro do. Winoma did this first. A number then means a place, and asking for one from the other monitor changes that monitor and not the one in front of the person, which Dante found confusing.
- **A Scratchpad per monitor.** Omarchy Quattro has one, shown on one monitor at a time.
- **Writing each Workspace's monitor to a file.** The file would outlive the windows it describes, and disagree with them after a crash. Where the windows are cannot be wrong.
- **Warping the pointer.** ADR 0034 holds: the pointer is the mouse's.
