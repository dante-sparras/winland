# Decisions

One line per ADR, by area. Read the ADRs for an area before changing how it behaves.

An ADR states one standing decision and why, in the present tense, and is true against the code. When a decision changes, rewrite its ADR in place and fix its line here. Git keeps the history. A new ADR takes the next number after the highest in any of the three repos, so a number names one decision in Winland, Winshell, and Winoma.

## Products

- [Winoma ADR 0176](https://github.com/dante-sparras/winoma/blob/main/docs/adr/0176-the-repo-builds-three-products.md) Winland, Winshell, and Winoma are three products. Winland owns every bind and matches Hyprland. The two processes talk over a private pipe, and Winland refuses a client that speaks another protocol.
- [Winoma ADR 0177](https://github.com/dante-sparras/winoma/blob/main/docs/adr/0177-three-products-are-three-repos.md) The three products are three repos. Winland publishes Winland.Core, Winland.Ipc, and Winland.Platform as packages, and Winshell and Winoma pin them. A bug in another product is an issue in that product's repo.

## Chords and the keyboard hook

- [0005](0005-super-l-is-layout.md) `Super+L` toggles Layout. `Super+Ctrl+L` locks, lifting `DisableLockWorkstation` first and setting it again after.
- [0017](0017-chord-ledger.md) The ledger's own key events are the only account of Super. Windows is never asked in order to clear it.
- [0064](0064-a-delivered-key-is-held-only-while-windows-holds-it.md) On each key-down the ledger asks Windows about the keys Windows was given, and forgets the released ones.
- [0020](0020-focus-does-not-join-while-super-is-held.md) A focus joins another thread's input only while no Super finger is recorded, and the keyboard is not dropped to the desktop then. The elevated helper never joins.
- [0028](0028-the-hook-is-installed-once.md) The keyboard hook is installed once, and again only on evidence, with a backoff.
- [0030](0030-the-hook-can-run-in-its-own-process.md) The keyboard hook runs in `Winland.Host.exe`. The shell pushes the state the hook reads, and the hook callback does not wait on the pipe.
- [0065](0065-the-elevated-helper-starts-when-an-administrator-window-comes-forward.md) An administrator window that takes the foreground starts the elevated helper, once, where the Host allows such windows. The helper is `Winland.Host.exe --place`, started by Winland. It moves, closes, and marks the window. Explorer hides it for the desktop.
- [0034](0034-the-pointer-shows-only-while-the-mouse-is-used.md) The pointer is drawn only while the mouse is used. A key hides it, and keyboard focus never moves it.

## Tiling and Workspaces

- [0003](0003-a-workspace-is-a-number-winoma-keeps.md) A Workspace is a number Winoma keeps. A switch shows the new windows, moves the keyboard, then hides the old ones. Winoma never touches a Windows virtual desktop.
- [0079](0079-each-monitor-has-a-workspace-in-front.md) Each monitor has a Workspace in front and one monitor has the keyboard, with Hyprland's rules for each Chord but one: `Super+N` brings a hidden Workspace to the monitor with the keyboard. A Workspace's monitor is read from its windows, never from a file. Keyboard focus never moves the pointer: a decision, not a Gap.
- [0021](0021-the-scratchpad-is-off-every-workspace-until-it-is-shown.md) The Scratchpad is on no Workspace. Winoma hides its windows itself, shows them over the Workspace with the keyboard, and hides it when another comes in front there.
- [0038](0038-a-new-window-opens-on-the-workspace-in-front.md) A hidden window that takes the keyboard or shows itself is followed. A new window opens on the Workspace with the keyboard, or on the one its program was started from, and a keyboard that fell to a hidden window is taken back.
- [0047](0047-tile-motion-is-decided-in-core.md) Core's `TileMotion` decides every tile move. The Win32 placer keeps the timer, the lock, and the facts about Windows.
- [0081](0081-a-window-fades-in-and-out-and-a-tile-glides.md) A window fades in as it opens and out as Winoma closes it, and a tile Glides on Quattro's times. The tiles that make room for a new window wait for its fade, and the ones that take a closed window's room take it at once. The 87% scale is not drawn.
- [0050](0050-a-desktop-that-starts-keeps-the-layout-on-the-glass.md) A starting desktop rebuilds each Workspace's tiles from its windows' rectangles, hidden or not, and brings back each Layout and Group from what the windows carry, never from a file.
- [0059](0059-the-desktop-writes-down-the-windows-it-hides.md) Winland hides a window with Explorer's cloak, or `SW_HIDE` where that is not used, after writing it into `hidden` with how. The word in `lifetime` says who shows it again: at once after a crash, and not at all across a replace. `Winland.Watch` waits for `Local\Winland` and shows the windows. `Winshell.Watch` waits for `Local\Winshell` and gives the taskbar back. Each watcher runs from a copy, so the live folder can be overwritten.
- [0074](0074-a-window-carries-the-caption-bits-winoma-took-off-it.md) The caption bits Winoma takes off a window are recorded on the window, as the property `Winland.Caption`.
- [0078](0078-a-window-carries-its-workspace.md) Each window carries its Workspace as the property `Winland.Workspace`. A desktop that starts puts every window back by it, and puts the Workspace whose windows are on the glass in front.
- [0076](0076-an-apps-fullscreen-covers-the-monitor-and-a-player-takes-a-tile.md) An app's own fullscreen covers the monitor. `Super+Ctrl+F` is tiled fullscreen, except for Chromium. `Super+T` tiles a player.

