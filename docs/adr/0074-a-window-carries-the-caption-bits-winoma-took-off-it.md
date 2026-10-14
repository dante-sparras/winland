# A window carries the caption bits Winoma took off it

Winoma takes the close, minimize, and maximize buttons and the thick frame off every tile: the Windows taskbar is hidden, so a minimized window would have nowhere to be restored from. Stop gives them back (ADR 0067). A record of what came off each window, kept in the desktop's memory, dies with a desktop that is killed. Every deploy kills the desktop, so the next one would find the tiles already stripped and its own Stop would give nothing back.

So the bits are kept on the window. When Winoma takes bits off a window, it sets the window property `Winland.Caption` to every bit it has taken off that window so far. Giving them back is a walk over the top-level windows: a window that carries the property, still has a caption, and lacks the bits gets them set, and the property is removed. The desktop does this when it stops. The desktop watcher does it when a crash leaves Winoma down, and `winoma stop` does it on a Host with no desktop. None of them needs anything from the desktop that took the bits.

A desktop that is replaced by another build keeps the windows as they are (ADR 0059). The next desktop is a moment away, and giving the bits back first would flash every tile. The next desktop needs no step to take the record over: it is on the windows. When none comes, the watcher gives the bits back.

A window that has gone fullscreen in its own app keeps its property and is left alone, since a frame put on it would show around the video. A window that has stopped answering keeps it too, for the next try.

Measured on Windows 11 build 26200 with three processes at the same integrity: a property set by one process on another's top-level window is read by a third after the first has exited, it stays through a style change and a frame change, and it is gone when the window is destroyed, with nothing left behind. A property of zero reads the same as none, so zero is never written.

## Considered options

- **A file beside `hidden` (ADR 0059).** It needs the owner's process id and start time to survive a reused handle, a write per stripped window, a line to forget for each closed window, and a step at start to take a dead desktop's record over. The property needs none of these, and adds no user file.
- **Give the bits back whenever the watcher sees the desktop exit.** Every deploy would restore every tile and strip it again a moment later.
- **Guess the bits of a window that is already stripped and carries nothing.** A window that never had a maximize button would get one.

## Consequences

Any program at the same integrity can read, change, or remove the property, and the window's own program sees it when it lists its properties. It is a note, not a guard: the worst a wrong value does is set a style bit on Stop. An administrator window is out of reach for the property as it is for the style change, and Winoma strips neither.

The frame color, the corners, and the border Winoma sets through DWM are still held in the placer's memory and are not given back after a kill.
