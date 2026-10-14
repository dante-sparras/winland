# A window carries its Workspace

A desktop that starts has to know which Workspace each window is on, and which windows are in the Scratchpad. A Workspace is a number Winoma keeps (ADR 0003), and the Scratchpad is Winoma's too, so Windows remembers neither. Every deploy and every Update replaces the desktop.

So the Workspace is kept on the window. Winoma sets the window property `Winland.Workspace` to the Workspace number, 1 to 9, or to 99 for the Scratchpad (`WorkspaceMark`). A window that is on no Workspace, a Sticky window or a Picture-in-picture player, carries none. The property outlives the desktop that set it and goes with the window (measured for ADR 0074), so a handle Windows hands to a later window names no old mark. A mark is read from the window and from nowhere else.

The shell writes the mark wherever a window's Workspace changes:

- A window that takes a tile carries the Workspace it is tiled on: the one with the keyboard for a window that just opened. Every arrange asks for every tile, and a window that already carries the number costs one read. This covers a new window, a Scratchpad window moved to a Workspace, a Sticky window that is tiled again, and a window that could not be hidden and so stays with the Workspace in front (ADR 0003).
- A window sent to another Workspace carries that Workspace, followed or not.
- A Free window that no window owns carries the Workspace it opened on (ADR 0003).
- A window sent to the Scratchpad carries 99, shown or hidden.
- A window made Sticky, and a player kept above the windows, have the mark taken off.
- A jump changes no mark, and neither does moving a Workspace to another monitor.

A desktop that starts reads the mark of every top-level window once, before it places or hides anything (`ShellController.AdoptMarks`). Hidden windows are read too. Nothing is read from a file.

- **Every window whose mark names a Workspace goes onto that Workspace**, when it is on the glass or hidden Winoma's way: under the shell's cloak, on a window Windows does not keep on another desktop of its own (`WindowHide.Cloaked`), or named by the record a replaced desktop left (ADR 0059). No record has to name it. A Workspace that is not in front has its tiles read from the rectangles its windows cover (ADR 0050).
- **The Workspace in front is the one whose windows are on the glass** (`WorkspaceFront`). With several monitors each has one, and a Workspace's monitor is read from where its windows are (ADR 0079). After a replace only its windows are. After a crash or a Stop every window is, so the Workspace of the window that has the keyboard comes first, then the one with the most windows on the glass, then the lowest number. With no marked window on the glass, it is the lowest number that holds no window, so an empty Workspace in front is not kept across a restart.
- **After a crash or a Stop the windows of every other Workspace are hidden again**, once the record names them. After a replace nothing is shown or hidden: the windows were never shown.
- A window on the glass that carries no mark takes a tile on the Workspace in front.
- **A marked window under Winoma's kind of cloak that no record names is this desktop's own.** A record can be lost or never written, and the window would stay cloaked with nothing to show it. It is kept hidden and written into the record when its Workspace is not in front or it is the Scratchpad's, and shown otherwise.
- A marked window that is off the glass some other way is left alone. Windows keeps it on another virtual desktop, or its program hid it, and it is not Winoma's until it is on the glass (ADR 0003).
- A window that carries the Scratchpad's mark goes back into the Scratchpad, closed. After a crash or a Stop it was shown (ADR 0059), and the first placement writes it into `hidden` and hides it again. If it had the keyboard, the Workspace gets it. After a replace it was never shown: the new desktop took it over still hidden, and it stays so. The windows go in by handle: the order they were sent in went with the old desktop.
- A Group is rebuilt, and a Workspace keeps its Layout, from the tiling mark the windows carry (ADR 0050). Its members carry their Workspace as well.

Stop leaves the marks, so a start after it finds them. Uninstall takes the property off every window after the Stop (`WindowProperty.RemoveFromAll`, in `winoma.exe`).

## Considered options

- **A file of handles and Workspaces, beside `hidden` (ADR 0059).** It is written on every send and every new window, it needs the owner's process id and start time against a reused handle, and a desktop killed between a move and the write leaves it wrong. komorebi and GlazeWM keep each window's workspace in their own process, and neither puts a window back after a crash.
- **Read it from the glass, as the tiles are (ADR 0050).** A rectangle does not say which Workspace a window is on, and a hidden window is not on the glass.
- **Read the Scratchpad from `hidden` (ADR 0059).** The record says which windows are hidden and how, not what hid them: a Group's hidden member is on it too. It is also gone after a crash or a Stop, when the windows were shown.

## Consequences

Any program at the same integrity can read, change, or remove the property. It is a note, not a guard: the worst a wrong value does is put a window on another Workspace or in the Scratchpad, one Chord away. A number Winoma never writes reads as no mark.

An administrator window refuses the property from a desktop that is not one, as it refuses `Winland.Caption`. It is hidden with its Workspace like any window (ADR 0003), so it needs its mark as much. The desktop hands a mark it was refused to the elevated helper, which writes it or takes it off (`WindowExecutor.Mark`, ADR 0065). The helper takes only what `WorkspaceMark` reads as a mark, and is asked once for each mark of a window.

Where no helper runs, the window carries no mark, and the log says `could not mark window N with workspace M` once: on a Host that does not tile administrator windows (ADR 0069), and where the person refused the helper. While the desktop runs the window keeps the Workspace Winoma has it on. A desktop that starts finds it with no mark: on the glass it joins the Workspace in front, and left hidden by a replaced desktop it is shown first (ADR 0059). It is never left hidden.

Uninstall runs without administrator rights (`winoma.exe`), so the mark the helper wrote stays on an administrator window until that window closes. It means nothing without Winoma.

A window that is in the Scratchpad when Winoma is stopped is shown, and keeps its mark. The next start puts it back in the Scratchpad.

After a crash or a Stop every window is on screen until the next desktop hides the Scratchpad's and the other Workspaces' again. Across a replace they stay hidden (ADR 0059). A Group's hidden members are shown either way, and the placement that follows hides them again.
