# The desktop cloaks the windows it hides, and writes them down

Winoma hides windows itself in three places: the windows of every Workspace that is not in front (ADR 0003), the Scratchpad while it is closed (ADR 0021), and a window a placement leaves no tile for, such as an inactive Group member. A desktop that is asked to Stop shows them on its way out. A desktop that is killed cannot, and a crash is a kill, and so is a deploy. Without a record the windows would stay hidden, unknown to the next desktop, with no chord to bring them back.

A desktop that is replaced by another build is the third case, and the common one: every deploy and every Update. Its hidden windows stay hidden, and the next desktop takes them over. Showing them in between would flash them onto the screen each time.

## How a window is hidden

There is one way to hide another program's window, and Core decides it (`WindowHide`):

- **Explorer's application-view cloak first.** `IApplicationView::SetCloak`, reached through the immersive shell's view collection (`ShellCloak`).
- **`ShowWindow` with `SW_HIDE`** for a window the cloak is not used on. That is the only fallback.

`CloakRefusal` lists the reasons to fall back: Explorer has no view of the window, Explorer refused, or Explorer could not be reached. Every reason hides the window with `SW_HIDE`, so a Host test that finds another window the cloak must not be used on adds a reason there and nothing else changes. The log names the reason and Explorer's answer for each window hidden without the cloak.

A window `SW_HIDE` does not take off the glass either is not hidden, and is not held as hidden: `WindowHide` forgets it and answers so, and the shell keeps it with the Workspace in front (ADR 0003). No window is known to end here. Windows refuses `SW_HIDE` for an administrator window asked by a desktop that is not one, and Explorer cloaks such a window, so one ends here only if the cloak is not used on it. A window that has stopped answering is asked without a wait and counts as hidden: it hides when it wakes.

`WindowHide` is also the ledger of what this desktop has hidden and how. A window is put back the way it was taken off, and the gateway, the placer, and the window watcher share the one ledger (`HiddenLedger`).

A cloaked window always has a way back. A window that is not on the ledger and is under Winoma's kind of cloak is uncloaked when it is shown: the shell's cloak, on a window Windows does not keep on a virtual desktop of its own (`WindowHide.Cloaked`). A window that is not cloaked is not asked anything.

What was measured on Windows 11 build 26200, in a line each:

- The cloak works from an ordinary process on another program's window, and costs about a millisecond.
- A cloaked window reads `DWM_CLOAKED_SHELL`, the value Windows gives a window on another virtual desktop. Windows still calls it visible.
- A cloaked window keeps the keyboard it had.
- Explorer cloaks and uncloaks an administrator window for an ordinary process. `SetProp`, `SetWindowPos`, and `SW_HIDE` on such a window are refused with access denied.
- Explorer is the door to the cloak and does not hold it. A cloaked window stays cloaked while Explorer restarts, and a view fetched from the Explorer that ended answers `0x800706BA`.
- A virtual-desktop switch does not undo the cloak.
- `DwmSetWindowAttribute` with `DWMWA_CLOAK` is refused for a window of another process. Winoma uses it only on its own panel window (`PanelVeil`).

The interfaces are not documented. komorebi and GlazeWM call the same ones, and their identifiers have not changed since 2023.

## What follows from the cloak

- **Explorer is reached once and asked again after a failure.** `ShellCloak` keeps the view collection, fetches a window's view for each call, and lets it go. A failure through a kept collection drops it, reaches Explorer again, and asks once more. A proxy belongs to the apartment that fetched it, so the desktop's thread keeps its own collection and the other threads share one.
- **The keyboard moves first.** Before Winland hides a window that has the keyboard, it gives the keyboard to a window that stays: on a switch, a window of the Workspace that comes in front (ADR 0003), and otherwise the Workspace's focused window (`BeforeHiding`). With no window to take it, the keyboard is dropped. While Super is held and no window can take it, the keyboard stays until Super goes up (ADR 0020).
- **The keyboard hook holds typed keys while the window that has the keyboard is under the shell's cloak**, Winoma's or Windows', and lets them go when that window is shown or the keyboard has moved. A switch moves the keyboard before it hides anything, so the hook holds nothing through one. It holds while a hidden window that took the keyboard by itself is being followed (ADR 0038). It starts no wait while Super is held: nothing typed reaches a window then, and a Windows chord would be stalled (`ChordLedger.Withhold`). It logs `keys waited 1000 ms` when keys waited a second: the line means a keyboard was left on a hidden window.
- **A window Winoma hid is shown before it is given the keyboard.** A Group member is focused a moment before the placement that shows it.
- **A cloaked window is on no list of the census.** The census lists the windows on the glass, and leaves out every window under a cloak, whoever put it there (`WindowAdmission.MayCount`). Winland keeps the windows it hid: each Workspace's, the Scratchpad's, and each Group's hidden members. A window Winshell tagged is never tiled and never hidden on a Workspace switch.
- **An owned window is left to its owner.** Windows documents that an owned window inherits its owner's cloak (`DWM_CLOAKED_INHERITED`), which is how a dialog follows its window between Windows' own desktops. So Winoma hides only windows that no window owns (ADR 0003). This is relied on from the documentation, and is not among the measurements above.
- **The census is read again after Winoma hides or shows.** Windows reports each cloak a moment later, on another thread. The gateway marks the census stale itself, so the arrange that follows a switch does not read the one taken before it.

## The record

Winland keeps a record of the windows it has hidden in `%USERPROFILE%\.config\winland\hidden` (`HiddenWindows`). It writes a window into the record before it hides it, and takes it out after it has shown it and the Host lists it again, so the record is never short of the glass. A switch writes the record once before it hides, and the arrange that follows writes it once more. A window that could not be hidden is taken out at once. A file with no windows is deleted.

A window is not hidden when its record could not be written. `WindowHide` asks for the record to name the window with the way it is about to be hidden (`IHideHost.Record`), and the file is written then if it does not. When that write fails, the window stays on the glass, with the Workspace in front, like one neither way takes off it.

Each line holds the handle, the process id, the process's start time, and how the window was hidden: `cloak` or `hide`. A window not hidden yet is written as `cloak`, the way every window is tried first. When the cloak is not used, the record is written again with `hide` before the window is hidden so.

## Who puts the windows back

Each product keeps its own `lifetime`. Winland's is `.config\winland\lifetime`, beside the record. The shell's is `.config\winshell\lifetime`. Each word says why that process ended (`Lifetime`):

- `running`: a desktop is up. One that exits under this word crashed.
- `stopped`: the desktop was asked to Stop (ADR 0067), or the session is ending.
- `replacing`: the desktop is ended so that another build takes its place. A deploy writes the word and then kills the desktop. An Update asks the desktop to leave (`host:replace`), and the desktop writes the word itself and exits without giving anything back (ADR 0068).

Each process starts its own watcher. The watcher reads the word once, when its process has exited, and does what it asks (`Lifetime.AfterExit`). Each waits for its own lock.

`Winland.Watch` is `Winland.Host.exe` under its own name (`WinlandWatch`). It runs from a copy at `%LOCALAPPDATA%\Winland\watch\w-<stamp>-<pid>`. It shows the windows Winland hid and starts the window manager again. The taskbar, the pointer, `Win+L`, and night light stay with the shell.

| Word | `Winland.Watch` |
| --- | --- |
| `running` | Shows the windows at once and starts `Winland.Host.exe` again. The caption buttons stay as the windows carry them, for the window manager that is starting. Three restarts inside a minute (`winland-restarts`) stop the chain, and then the caption buttons come back. |
| `replacing` | Shows nothing, and waits up to 30 seconds for a window manager that holds `Local\Winland`. When one comes, the watcher exits. When none comes, it shows the windows, gives the caption buttons back, and leaves Winland down. |
| `stopped` | Shows whatever the record still names, gives the caption buttons back, and leaves Winland down. |

`Winshell.Watch` is the shell under its own name (`DesktopWatch` while Winoma is loaded). It runs from a copy at `%LOCALAPPDATA%\Winshell\watch\<stamp>-<pid>`. It starts the shell again and gives Windows back what the shell took. The windows stay with Winland's watcher.

| Word | `Winshell.Watch` |
| --- | --- |
| `running` | Starts the shell again. The pointer comes back at once. |
| `replacing` | Shows nothing, and waits up to 30 seconds for a shell that holds `Local\Winshell`. When one comes, the watcher exits. When none comes, it gives the taskbar, `Win+L`, and the ramp night light back, and leaves the shell down. |
| `stopped` | Gives the taskbar, `Win+L`, and the ramp back, and leaves the shell down. |

Wherever `Winshell.Watch` gives the taskbar back it also gives back the ramp night light left on the monitors, when the `warmth` record names one (ADR 0083). The pointer comes back as soon as the shell has exited, under any word (ADR 0034).

A process that already holds its lock is left everything, under any word. `Winland.Watch` takes only the record its own window manager wrote (`HiddenWindows.WrittenBy`), because a newer window manager may have taken the record over, or be hiding windows of its own.

A watcher of a replaced process reads `lifetime` in the same milliseconds in which the next shell writes it. So every reader and writer opens the file shared, and asks six times over a third of a second for a file another program holds (`Lifetime.TryRead`, `Lifetime.TryWrite`). Each watcher reads first thing after the exit, once, and never again while it waits.

- A word that could not be read is `running`: Winland shows the windows, and each watcher starts its process again.
- A desktop that could not write its word keeps running and keeps its watcher. The log says which word still stands, and the desktop writes again every second until the write lands, so a desktop that is up does not leave `replacing` in the file. Until it lands, an exit is acted on under the word that stands.
- A desktop that exits on purpose tries a word it still owes once more first.

The shell reads its own word, then writes `running` into `.config\winshell\lifetime`. It starts Winland with no arguments. Winland reads `.config\winland\lifetime`. A replace writes `replacing` into both files before the kill, so a file that already says `running` does not turn a replacement into a crash. Winland still accepts `--inherit <word>`; the shell and a swap do not pass it:

- **After `running` or `stopped`** Winland puts back every window a record still names, once it holds `Local\Winland`. This covers a watcher that was killed too. Winland then hides the windows of every Workspace but the one in front again, and the Scratchpad's (ADR 0078).
- **Under any word** Winland then takes every marked window it finds under Winoma's kind of cloak that the record did not name, and settles it the same way (ADR 0078). The record is one way to find a hidden window, and the window itself is the other.
- **After `replacing`** it shows nothing. Winland takes the record over (`HiddenStore.Inherit`), including when it was started with `--inherit replacing`: each window that is still hidden goes on its ledger with the way the record gives, and the record is written again under the new window manager's process id. It then settles each of those windows as it first places windows (`IWindowGateway.LeftHidden`). A window it keeps hidden stays as it is, and every other is shown, before the record lets go of it. The shell takes the record only when `Winland.Host.exe` is not running.

What Winland keeps hidden is every window whose mark names a Workspace that is not in front, and every window that carries the Scratchpad's mark (ADR 0078). A window tagged `Winshell.Window` is not among them. A hidden member of the Workspace in front is shown, joins its Group again (ADR 0050), and is hidden again by the placement that follows. A window on the record that carries no mark is shown: nothing of the new desktop would ever show it again.

A kill at any instant leaves every hidden window on a record that some program will read:

- Winland writes a window into the record before it hides it, and takes it out after it shows it.
- A takeover replaces the record in one move. Until then the record is the old desktop's, and whole.
- The shell writes `running` after Winland has been started with the word. One that dies after that write, and before the takeover, died under `running`, so the next start shows every window the record names.
- After the takeover the record is the new window manager's, and `Winland.Watch` answers for it.

A window is put back only when it is still open, still owned by the process that owned it when it was hidden, and still hidden the way the record says: the shell's cloak is on a window recorded as `cloak`, or a window recorded as `hide` is not visible. A cloak is taken off through Explorer, by any process, and a hidden window is shown. Windows hands out a closed window's handle again, and a process id too, and after a restart of Windows every handle in an old file names something else.

The record is deleted once every window it names is back. It stays when Explorer did not take a cloak off, so the next reader asks again. A desktop that is asked to Stop does the same: a window Explorer did not uncloak stays on the record, the log names it, and the watcher asks again under `stopped`. A rescue waits up to three seconds for an Explorer that cannot be reached, since a desktop can die with it.

ADR 0050 turned down a file for the tile tree for two reasons: it would be written on every change, and handles go stale. Neither holds here. The hidden set changes on a few chords, not on every arrange, and the start time catches a stale handle. The glass cannot stand in for this file the way it does for the layout, because a hidden window is the one thing the glass does not show.

A window that was shown goes back where its mark says, whether it was shown in between or not: onto its Workspace, hidden again when that one is not in front, or into the Scratchpad (ADR 0078).

## Consequences

- **Each watcher of a replaced process runs from a copy** under its product's `watch\` folder, so it does not hold the live folder while it waits. The shell's folder is `<stamp>-<pid>`. Winland's is `w-<stamp>-<pid>`. Winland removes only its own old `w-` folders, and the shell removes only its own. Neither removes the other's, and neither removes a folder whose executable is still running. `Directory.Delete` on a folder a process still has open deletes every file that process has not locked and then fails on the executable. A `Winland.Watch.exe` left without `hostfxr.dll` is a WinExe: it shows "You must install .NET", `CreateNoWindow` leaves that dialog hidden, and the process never reaches the wait, so it does not exit when `Local\Winland` is held. A copy that lost `hostfxr.dll` while the live folder still has it is not used; the watcher runs from the live folder. The watcher does not call `WaitForExit`. It asks until the process id is gone, has exited, or was given to a process that started later, and then reads `lifetime`. Windows lets a running program's file be renamed, and refuses to overwrite or delete it. A file the copy still cannot overwrite is renamed to `*.old` and copied again (`AGENTS.md`). An Update moves the whole build aside file by file, as it did before (ADR 0068). When the copy of a watcher cannot be made, that watcher runs from the live folder, and the rename is what lets the swap finish.
- **Each copy has 30 seconds.** A deploy publishes to another folder first, while the old desktop runs. A replace that takes longer shows the hidden windows once and the taskbar once, and the processes that then start hide them again.
- **A restart is a Stop and a start** (ADR 0067), so it still shows every hidden window between the two.
- **Each watcher loads its copy's assemblies before it waits**, so a swap of the live folder does not reach it. A file `Winland.Watch` first loads when it shows the windows is read from that copy.

## Considered options

- **Turn the window's redraw off (`WM_SETREDRAW`).** The Scratchpad did this. It is a message to the window's program with a wait on its answer, a window that did not answer was hidden some other way, and Explorer dropped its view of the window, so a later move between virtual desktops had to repair it.
- **`SW_HIDE` for every window.** A placement did this. The window's program is told it was hidden, and komorebi and GlazeWM both moved off it after programs misbehaved under it.
- **Two ways, one per caller.** Each needs its own undo in the rescue and its own rule for the keyboard.
- **The watcher shows the windows whatever the word.** It did, and the next desktop hid the Scratchpad's again a moment later. With every Workspace but the one in front hidden (ADR 0003), that is every window of every other Workspace on the screen at each deploy.
- **No wait: only a desktop that starts shows the windows of a replaced one.** A deploy whose copy failed, or an Update whose new build never started, would leave them hidden with no program to show them.
- **The deploy or the Update shows them when its start fails.** Each runs in a shell that can be closed or time out. The watcher is the one program that is always there when a desktop exits.
