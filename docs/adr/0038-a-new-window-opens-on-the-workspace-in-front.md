# A hidden window that shows itself is followed, and a new window opens on the workspace in front

Winoma hides the windows of every Workspace that is not in front (ADR 0003). A program does not know that. It brings its window forward when the person clicks its Notification, picks it in `Alt+Tab`, or hands it a link, and the window takes the keyboard while it is still hidden.

## A hidden window that shows itself

A window Winoma keeps hidden that takes the keyboard, or that its program puts back on the glass, is followed (`ShellController.FollowShown`). Omarchy sets `focus_on_activate` for the same reason.

- A window of another Workspace: that Workspace comes in front on its own monitor, and the window has the keyboard.
- A Scratchpad window: the Scratchpad is shown (ADR 0021).
- A Group member behind the one shown: it becomes the member shown.

Winoma decides before the window is drawn. The window is still hidden when Winoma finds it, and the switch that shows it is Winoma's own (ADR 0003). The window watcher wakes the desktop when the keyboard moves to a window the desktop hid, and the loop looks again on every turn. The keyboard hook holds what is typed until the window is on the glass (ADR 0059).

## A new window

A window that just opened is on no Workspace, and takes a tile on the one with the keyboard, as Omarchy opens a new window on the active workspace. A Chromium browser that starts again restores its windows as new windows, so they open where the person is, and not where the browser was last used. Only a desktop that starts reads a mark (ADR 0078): a mark a new window happens to carry does not move it.

## A window whose program Winoma started

A program takes a moment to open its window, and the person may have gone to another Workspace or another monitor by then. A window whose program Winoma started opens on the Workspace that had the keyboard at the start, as Hyprland's initial workspace tracking does. Omarchy Quattro turns that tracking off (`initial_workspace_tracking = 0` in its `looknfeel.lua`), so there the window opens wherever the keyboard is by then. Winoma departs from Quattro here because Dante asked for it on 2026-10-11. This holds for a start from the Menu, and for the terminal, browser, files, calculator, and `app.<role>` Chords.

- That Workspace is in front on another monitor: the window takes a tile there.
- That Workspace is hidden: the window joins it and is hidden with it.
- Either way the keyboard stays where the person went. A new window takes the keyboard as Windows draws it, so for two seconds (`ShellController.LaunchedKeyboardMs`) the keyboard on that window is taken back once, and not followed. After that it is a pick, and is followed.

Windows does not say which start a window came from. `LaunchAsks` keeps each start for ten seconds, and the next new tile takes the oldest. A program that only brings its open window forward opens none, so its start runs out. Until it does, another window that opens takes it: a new window made on another Workspace within those ten seconds opens where the program was started.

## A new window is first drawn on its tile

Windows makes a program's first window on the primary monitor, whichever monitor has the keyboard. Winoma hears of a window when it is shown, so a tile placed then leaves the window in the wrong place for a moment first. With several monitors the window watcher therefore also listens for a window being made, and `ShellController.Seat` puts one that is not shown yet on the tile it will take on the Workspace it will join. The arrange that follows the show adds it to the Workspace and finds it there.

- The tile is the one `TilingSession.Add` would give it, read without adding it (`TilingSession.Preview`): a program makes many windows it never shows, and one that held a tile would leave a hole. The other tiles make room only once the window is shown.
- The window rectangle for that tile needs the window's border, and DWM may not have measured a window it never drew. Its sizing border is then read from its style (`WindowPlacer.OuterUnshown`).
- Where the Workspace has no tile to name, the window is only moved onto its monitor, as far from that monitor's corner as it was from its own (`OpeningSeat`): a Workspace with a group strip or a fullscreen window, and a tile off the glass in the scrolling layout.
- Only a top-level window with a caption or a sizing border that no window owns is moved (`WindowGateway.MayBecomeTile`).
- A window that a hidden Workspace will take is left alone: it is hidden as it opens.
- A program that puts its window somewhere itself after making it, such as one that restores where it was last closed, is still drawn there first.
- With one monitor nothing is moved, and the desktop's thread is not woken.

Measured on Dante's Host on 2026-10-11 from a recording of the second monitor, Windows Terminal started with the keyboard there: its first frame is on its tile, and its window rectangle is on the tile from the first read. Before, it was first seen on the primary monitor, and after the monitor was right but not the tile, 78 ms passed between the first frame and the tile.

## A keyboard that fell to a hidden window

When the window that has the keyboard closes, hides, or is minimized, Windows hands the keyboard to the next window in its own order, hidden or not. That window did not show itself, and following it would take the person to another Workspace each time they closed a window.

So the Host notes, as the keyboard moves, whether the window it left was already off the glass (`KeyboardTrail`, in Core, fed by the window watcher). Core takes the keyboard back for the Workspace in front when that window was a tile or an overlay of this Workspace, a window that has closed, or a Panel of Winoma's that closed within the last second and a half (`ShellController.HandoverMs`). The keyboard goes to the Workspace's focused tile, or to no window when it has none.

Windows' own switcher and Start leave the glass too, after the person picked a window in them. They are none of the windows above, so the window picked there is followed. A window brought forward while the window before it is still on the glass asked for the keyboard, and is followed.

With several monitors the next window in Windows' order can be on the glass, on the Workspace in front on another monitor. A window there that takes the keyboard moves the keyboard to its monitor (ADR 0079), so this one is told apart the same way: when the window the keyboard left has closed, the keyboard goes back to the Workspace that had it.

A window Winoma hid with the keyboard still on it did not show itself either. After a switch to an empty Workspace made with Super held, the keyboard waits on the hidden window until Super goes up (ADR 0020). It is followed only once the keyboard has left it and come back.

## Considered options

- **Windows moves first.** While a Workspace was a virtual desktop, Windows switched desktop when a window on another one took the keyboard, and Winoma followed after the fact. A new window that pulled Windows away was moved to the Workspace in front and Windows was switched back, with the wrong Workspace on screen for a moment.
- **Follow every hidden window that has the keyboard.** Closing the last window of a Workspace, or closing a Panel on an empty one, would send the person to whichever Workspace Windows picked a window from.
- **Naming the monitor to Windows as the program is started** (`SEE_MASK_HMONITOR`). It reaches only a program Winoma starts directly. A start through File Explorer or an app execution alias, as Windows Terminal's is, loses it.
- **Matching a new window to its start by its program's file.** A start through File Explorer gives no process, a packaged program has no file, and many programs open their window from another process than the one started.
- **Giving the started window the keyboard on its Workspace.** The person left that Workspace on purpose, and would be pulled back to it.
- **Follow none.** A click on a Notification, a pick in `Alt+Tab`, and a link handed to a running browser would each leave the keyboard on a window nobody can see.
