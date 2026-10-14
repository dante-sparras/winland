# A Workspace is a number Winoma keeps

A Workspace is a number from 1 to 9, and Winoma keeps which windows are on it. Each monitor has one Workspace in front (ADR 0079). The windows of a Workspace in front are on the glass, and Winoma hides the windows of every other (ADR 0059). Windows has one desktop as far as Winoma is concerned: Winoma never makes, removes, or switches a Windows virtual desktop, and Task View shows none of Winoma's Workspaces. `Alt+Tab` stays Windows' switcher.

Where a window is, is Winoma's own record: the Workspace that holds its tile (`WorkspaceTravel`), written on the window as its mark so that it outlives the desktop (ADR 0078). The window census lists only the windows on the glass (`WindowAdmission.MayCount`), and never says which Workspace a window is on.

## A switch

A number Chord, `Super+Tab`, a click on a Workspace in the Bar, and `winoma` all end in one call, `ShellController.Switch`. With one monitor a switch takes one Workspace off the glass and puts another on it. With several, the same call serves every change of the Workspaces in front (ADR 0079), and a keyboard that goes to a Workspace already in front on another monitor shows and hides nothing. Its order is fixed:

1. The record of hidden windows takes the windows that are about to be hidden (ADR 0059).
2. The new Workspace's tiles are placed while its windows are still hidden. A window that was sent there lands on its tile unseen. The placement names the tiles of every other Workspace in front too, since the Host keeps the last placement as the whole glass.
3. Its windows are shown, in one call to the Host.
4. The keyboard moves to one of them: the window that came with the person, or the tile under the pointer, or the Workspace's focused tile. An open Panel keeps the keyboard (ADR 0035).
5. The old Workspace's windows are hidden, in one call to the Host. A Scratchpad that was shown over it is hidden with them (ADR 0021).

A hidden window keeps the keyboard it had, so the keyboard has left before anything is hidden. The shell asks the Host for no frame, no census, and no window's answer between the show and the hide, so both reach the screen together, and a switch costs about a millisecond a window (ADR 0059). The arrange that follows reads the Host again, and finds a window that opened or closed meanwhile.

- **With Super held.** A switch is made with Super held, and a focus then joins no other thread's input (ADR 0020). When Windows refuses the focus, or the Workspace has no window, the keyboard is dropped to the desktop's own window. While Super is held it cannot be dropped either (ADR 0020), so it stays on the window that was just hidden until Super goes up. The Workspace's focused tile is asked once more then, and the keyboard is dropped only when it refuses again.
- **A sent window.** A window sent to another Workspace without following is hidden after the keyboard has moved to a window that stays. A window sent and followed is never hidden: it is placed with the Workspace it goes to, and keeps the keyboard.

## Which Workspaces exist

A Workspace exists while it holds a window or is in front on a monitor (`WorkspaceBook`). A number is only a key, so the numbers that exist may leave holes.

- A number Chord for a Workspace that does not exist makes just that one.
- An empty Workspace stops existing once it is in front on no monitor. One whose last window closed while it was hidden stops existing then.
- `Super+Tab` and `Super+Shift+Tab` visit the Workspaces that exist, on every monitor, in number order, and wrap.
- The Bar and `winoma desktop` list the first four, and every other that exists: 1 2 3 4 7.

A Workspace belongs to one monitor, and its windows are tiled there. Each monitor has one Workspace in front, one monitor has the keyboard, and `Super+Shift+Alt+Arrow` moves a Workspace to another monitor. ADR 0079 has what each Chord does across monitors.

A Free window takes no tile and is on a Workspace all the same. One that no window owns is hidden and shown with the Workspace it opened on, and carries that Workspace's mark. One that another window owns is left to its owner: Windows cloaks an owned window with its owner. A Free window that no window owns makes its Workspace exist, as a tile does: the Workspace would otherwise leave the Bar and `Super+Tab` while its window stayed hidden, and its number could be given to another monitor.

## What is never hidden

The Bar and a Panel are Winoma's own windows. A Sticky window and a Picture-in-picture player are on no Workspace. No switch hides any of them, so nothing has to keep them on the glass.

## An administrator window

Explorer cloaks an administrator window for any process, so it is hidden and shown with its Workspace like any window (ADR 0059). Windows refuses the rest from a desktop that does not run as administrator: a move, a close, a window property, and `SW_HIDE`.

- **With the elevated helper** (ADR 0065) the window is a tile. The helper moves and closes it, and writes its mark when the desktop is refused (ADR 0078), so it is back on its Workspace after a restart like any window.
- **On a Host that does not tile administrator windows** (ADR 0069) no helper runs. The window stays Free, and is hidden and shown with the Workspace it opened on. Nothing can write its mark.
- **A window that carries no mark is never left hidden.** While the desktop runs it keeps the Workspace Winoma has it on. A desktop that starts shows one that a replaced desktop left hidden (ADR 0059), and it joins the Workspace in front.

## A window that cannot be hidden

A window is hidden with Explorer's cloak, and with `SW_HIDE` where the cloak is not used (ADR 0059). A window neither takes off the glass is not held as hidden (`WindowHide`): it is on the glass, so it is on the Workspace in front. It leaves the Workspace or the Scratchpad that held it, the record lets go of it at once, and the next arrange gives it a tile where the person is. Such a window travels with the person. No window is known to be one. An administrator window would be, on the day the cloak is not used on it.

## Windows' own virtual desktops

Winoma works on the Windows desktop that is in front, and leaves the others alone. A window Windows keeps on another virtual desktop reads the same cloak as one Winoma hid, so the cloak does not say whose it is.

- Winoma takes a hidden window for its own only when its own record names it: the Workspace that holds it, and for a desktop that starts, the mark together with the record of hidden windows (ADR 0078).
- The census leaves out every cloaked window. A window on another Windows desktop is on no Workspace until it is on the glass, and then it joins the Workspace in front, as any window that opens does.
- A window Winoma shows that does not come onto the glass is one of two things. Winoma asks Windows through `IVirtualDesktopManager`, the interface Windows documents (`WindowsDesktops`). A window on another Windows desktop leaves its Workspace. Any other is asked to show again at each arrange, and stays on the record until the Host lists it.

## Considered options

- **A Workspace is a Windows virtual desktop.** Winoma did this. A switch slid where Omarchy Quattro cuts, every monitor switched together, and Windows switched by itself when a window on another desktop took the keyboard, so Winoma followed after the fact. Creating and switching desktops also needs an interface Windows does not document, which changes between Windows builds and stops answering while Explorer restarts.
- **Create every Workspace up to the number asked for, and never leave a hole.** A virtual desktop forced this. A number that is only a key needs neither.
- **Hide every window that is not a tile of the Workspace in front.** A tool window, a tooltip, or a window with no caption would be hidden too, and Winoma leaves those alone (Admission).
