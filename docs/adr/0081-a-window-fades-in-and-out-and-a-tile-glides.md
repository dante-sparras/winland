# A window fades in and out, and a tile Glides on Quattro's times

Windows move on Omarchy Quattro's times and curves, read from the `hl.curve` and `hl.animation` lines of its `looknfeel.lua` (`MotionCurve`). A speed of 1 there is 100 milliseconds.

| What | Quattro | Winoma |
| --- | --- | --- |
| A window opens | `windowsIn`, 410 ms, from 87% of its size, and `fadeIn`, 173 ms | Fades in over 173 ms on `almostLinear`. It does not grow |
| A window is closed | `windowsOut`, 149 ms, to 87%, and `fadeOut`, 146 ms | Fades out over 146 ms on `almostLinear`, when Winoma closes it. It does not shrink |
| A tile moves | `windows`, 379 ms on `easeOutQuint` | A Glide of 379 ms on `easeOutQuint` |
| A tile changes size | The same, with the window's picture stretched | The same Glide, with the window laid out again on the way |
| A Workspace change | Off | None |

It all follows the Windows animation setting. With animation effects off, a window opens and closes at once.

## The fade is drawn by Windows, on the window itself

`WindowFade` makes the window a layered window with an opacity for as long as the fade lasts, and an ordinary window again after it. Windows draws the fade with the window's own backdrop, at the display's rate, and nothing of Winoma's is drawn over the window.

- **Opening.** The window watcher hears a window being made, before its program shows it. A top-level window with a caption or a sizing border that no window owns is made clear then (`WindowGateway.MayBecomeTile`). When it is shown, or uncloaked, it fades in. Its first frame is the first of the fade.
- **Closing.** `WindowGateway.Close` fades the window out and asks it to close when it is clear. So only a close Winoma makes fades: the close Chord, the Menu, and close all. A window that closes itself is gone before anything can be drawn of it.
- **No window stays clear** (`WindowFades`). One that is not shown within five seconds is solid again. One that is still open a second and a half after it was asked to close is solid again, and at once when a dialog of its own is in front of it, since it is asking whether to save.
- **A clear window carries the property `Winoma.Fade`.** A desktop that starts makes every window solid that carries it, and so does the watcher of a desktop that died (`WindowFade.RestoreLeft`). The mark is written before the window is made clear.
- **A window that is already layered is left alone.** Its program draws it that way. So is one that refuses the change, as an administrator window does.
- **The style change waits on the window's own thread**, which is busy starting its program. It runs on a thread of its own, as the move onto the tile does (ADR 0038), so one program never holds the thread that hears window events or the desktop's thread. A program that showed its window while the change waited keeps it solid: making it clear then would blink it out.

## A tile Glides, and when it sets off is timed against the fades

A tile that moved, changed size, or both Glides: its four edges are eased over 379 ms (`TileReturn`). So a tile that swaps with one of another size travels and takes the new size on the way.

- **A window opens.** The tiles that give it room stay as they are until the new window has faded in above them (`MotionCurve.OpenFadeMilliseconds`), and Glide then. Nothing shows through the new window while it is still clear, and a new window with an acrylic backdrop has the same thing behind it for the whole of its fade.
- **A window leaves the tiles**: it is closed, sent to another Workspace, or floated. A tile that takes its room and changes size takes it at once (`TileMotion.Left`). A window that is closed fades out above it, and the fade uncovers it. A window that closed itself is already gone, and has left the wallpaper bare. A tile that only moves still Glides.
- **A window closes itself.** The wallpaper shows for about 40 ms. The turn that follows a window event waits at most 40 ms after the turn before it (`TurnFloor.FloorMs`), and a turn takes about a millisecond.

A program lays its content out again for a new size, and that is its own time. Measured on Dante's Host on 2026-10-11 with Brave playing a video in the neighbouring tile, the page shows its old layout for about 500 ms after the last size it is sent, whether it was sent one size or forty. So a Glide that changes a browser's size is followed by the page catching up half a second later.

## Considered options

- **Windows' own open and close transitions.** Winoma switches them off on every tile, so a minimized or maximized window it restores does not animate under the placement. Switched back on for one window, they still did not play: a tile has no caption, and Windows animates a window that has one.
- **A live picture of the window in an overlay** (a DWM thumbnail), scaled and faded, with the real window clear underneath. It gives Quattro's 87% scale, and it was built as a throwaway and measured. The picture of a clear window is still drawn, so it works. It was turned down for now: the picture leaves out an acrylic backdrop, so Windows Terminal brightens when the real window takes over, the overlay has to sit between the tiles and the windows above them, and a picture keeps its proportions, so it cannot stretch a tile that changes size. The scale is the part of Quattro's motion Winoma does not have.
- **Cloaking the window instead of making it clear.** A cloaked window is hidden to the census, so the Workspace would give its tile away while it fades.
- **A new size in one step, always.** The page catches up 379 ms sooner. It was built and Dante turned it down on 2026-10-11: a tile that swaps with one of another size jumped, and the window behind a new terminal changed all at once through the terminal's backdrop.
- **Gliding the tiles that take a closed window's room.** Recorded: the window fades faster than they arrive, and the wallpaper shows between them for about 90 ms.
- **Gliding the tiles as the new window fades in**, as Hyprland does. The wallpaper would show through the window while it is still clear.
