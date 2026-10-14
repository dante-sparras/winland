# An app's own fullscreen covers the monitor, and a player takes a tile

A window its own app makes fullscreen, such as F11 or a video's fullscreen button, covers the monitor, as `Super+F` does. When the app leaves fullscreen, the window takes its tile again. Omarchy does the same: it suppresses only a client's maximize request, so Hyprland puts a client that asks for fullscreen over the monitor. `Super+Ctrl+F` is tiled fullscreen, for every app but a Chromium one. A video on a tile is the browser's picture-in-picture player, which `Super+T` gives a tile.

## A window that goes fullscreen

An app that goes fullscreen drops its caption and sizes its window to the monitor. The window census leaves a captionless window out, so the shell sees a window of the session that is gone from the census and still on screen.

- A captionless window that fills the monitor becomes the Workspace's fullscreen window, once. It is the `Super+F` window from there: placed on the monitor, drawn over the bar, and reported as `fullscreen` by `winoma desktop`. It keeps its place in its Workspace's tiles, so the bar's workspace icons still list it.
- A captionless window that does not fill the monitor keeps its tile.
- A window with a caption is always a tile, even when it fills the monitor. An app that reopens at the size it saved while fullscreen is tiled.
- When the caption is back, the app has left fullscreen, and the window is tiled again, whether the app or `Super+F` made it fullscreen.

The bar stays where it is under a window that fills the screen, and is in front again when the window no longer crosses it. `Super+Shift+Space` still remembers a hidden bar.

## Tiled fullscreen

`Super+Ctrl+F` is Omarchy's tiled fullscreen: the app is fullscreen, and its window stays on its tile. Windows has no message that tells an app it is fullscreen, so Winoma presses the key most apps answer for it, F11, in the focused window, behind a still picture of the monitor. The shell remembers briefly that it asked, and leaves that window on its tile when it goes fullscreen. An app that does not answer F11 does nothing, and the asking is forgotten.

- The chord toggles. On a window that is fullscreen in its app, on its tile or over the monitor, it presses F11 again. On the window `Super+F` put over the monitor, it tiles the window and presses nothing.
- The key goes where the keyboard is, so the chord is refused unless the focused window has it. Control+F11 is another key to the app, so the key waits for Shift, Control, and Alt to come up, and then lets them go itself.
- `Super+F` on a window that is fullscreen in its app moves it between the monitor and its tile, and the app stays fullscreen. `Super+F` never presses a key in an app.
- Omarchy binds no F11. A plain F11 the person presses reaches the app and covers the monitor.

## A Chromium window is left to its app

Fullscreen in Chromium is the monitor. Each time the window gets the keyboard or loses it, Chromium gives the window the monitor's rectangle and lays the page out for it, and nothing Winoma can do from outside the browser stops that. So a Chromium window that is fullscreen in its app is never held on a tile.

The gateway says which apps these are: the ones whose top-level windows have Chromium's class, `Chrome_WidgetWin_1`. That is Chromium, the browsers built on it, and Electron apps. Such a window, while it has no caption:

- is not moved by a placement, and is not asked whether it sits on its tile;
- is not pulled back by the window motion watcher;
- is not kept above the other windows, so a window that takes the keyboard from it is seen above it;
- keeps its place in its Workspace's tiles, and takes its tile when its caption is back.

`Super+F` does nothing to it, and `Super+Ctrl+F` asks nothing of such an app, fullscreen or not. A plain F11 reaches it as it is. Firefox can make a page fullscreen inside its window (`full-screen-api.ignore-widgets`), and is then held like any other app.

## Holding a window on its tile

- **The move.** An app that is fullscreen can answer every move it is asked about with the monitor's rectangle. Winoma moves a captionless tile with `SWP_NOSENDCHANGING`, which skips the question, and alone, because a deferred batch rejects that flag.
- **The watcher.** The window motion watcher pulls back only a window that was captionless when it was last placed on its tile. A window that has just gone fullscreen is left where its app put it until the shell covers the monitor with it, so it is never drawn in its tile on the way.
- **The still picture.** A plain F11 that leaves fullscreen on a held tile, and the `Super+Ctrl+F` key, run behind a still picture of the monitor: the app sizes itself before Winoma can put it back, and a frame of that would reach the screen. The hook keeps the key, the picture is put above every window and drawn, and then Winoma sends F11. The picture lifts once the window has stayed on its tile for a moment. A second F11 while it is up is dropped. F11 that enters fullscreen, F11 with a modifier, and F11 on a window that is not a tile reach the app at once. A keyboard hook in its own process (ADR 0030) does not freeze, because it cannot ask the desktop inside the hook.
- **The copy.** The picture is taken through DXGI desktop duplication in linear scRGB and divided by the monitor's SDR white level. A GDI copy misses the Mica and acrylic backdrops, and an 8-bit duplication with HDR on is drawn brighter than the screen. A new duplication has no image until the screen changes, so Winoma changes one corner pixel by an invisible step of alpha.
- **The frame.** An app leaving fullscreen can put its resize frame back, as Windows Terminal does. The watcher takes the frame off at once. Windows Terminal keeps its content laid out for the old client until it is resized, so Winoma resizes the window by a pixel and back, and pulls its XAML content bridge onto the client when the app resizes it late.

## A player on a tile

A browser's picture-in-picture player is an ordinary window with a caption, titled “picture in picture”, that the browser keeps above the other windows. Being kept above is what makes it Picture-in-picture to the census. A window with that title that is not kept above is a tile.

- `Super+T` on a player that has the keyboard stops Windows keeping it above the others. The next census finds a window like any other, and it gets a tile on the Workspace in front and is hidden with that Workspace from then on. Nothing in the shell knows it is a player from there.
- `Super+T` on a tile whose title is the player's keeps it above the others again. It leaves the tiles at once, gets back the frame and buttons Winoma took off it as a tile (ADR 0074), and goes back to the rectangle it had when it took the tile. The gateway remembers that rectangle in memory, so after a desktop that was killed the player floats at the size of its tile.
- The state is on the window, as the caption bits are: a desktop that starts tiles a player that is not kept above, and leaves one that is on the glass through every Workspace switch.

## Measured, and not worth trying again

On Brave, 2026-10-10, unless another app is named.

- A fullscreen Chromium window answers every `WM_WINDOWPOSCHANGING` with the monitor's rectangle, and retakes that rectangle on every focus gain or loss.
- After it loses the foreground to a window on the same monitor it sits one pixel row short and lays its page out only for a size it was asked about. A move that skips the question then changes the window and not the page.
- Posting `WM_ACTIVATE` flips that state both ways, and the two states cannot be told apart from outside.
- `SWP_FRAMECHANGED` with no size changes neither the window nor the page.
- `DWMWA_CLOAK` on another program's window answers `E_ACCESSDENIED`. There is no cloak during a move, and no DWM thumbnail stand-in for a window kept out of view.
- `SWP_HIDEWINDOW` for the move: the window keeps the keyboard, takes the monitor unseen, and the page lays out both sizes when it is shown.
- Making the window one row shorter before the move does not swallow the monitor's size: the page draws the change in about 40 ms.
- An opaque cover over the monitor makes the page count as hidden within 60 ms, and for up to five seconds after the cover is gone. A cover the size of the tile hides nothing.
- A picture over the tile had to stay 1.3 s to hide the monitor's layout on a YouTube page, and one run took 2.5 s.
- Windows does not lower the window that has the foreground: the call succeeds and the order stays.
- Chromium has no setting and no launch switch for the fullscreen rectangle. What is left is code put into the browser, a display driver, or the debugging port.
- Chromium resizes its `Intermediate D3D Window` child and never moves it.
- Picture-in-picture starts only from a gesture in the page. Nothing outside the browser can ask for it.
- The player took 1284x767, 1280x1529, 2000x700, and 640x360 as given, and did not move or change size over twelve focus moves.

## Considered options

- **Hold every fullscreen window on its tile.** Omarchy does not by default, and the page is laid out by the app.
- **Hold a Chromium window with a picture over its tile.** It hides a page that is wrong for a second after every focus move, for as long as one page took on one machine.
- **Make a plain F11 tiled fullscreen.** No Omarchy release did. v1.6 bound F11 to the compositor's fullscreen, v2 and v3 bound `Shift+F11` to it, and Quattro binds neither.
- **Send Escape to leave fullscreen.** Escape closes a dialog or stops a page load in an app that is not fullscreen. F11 toggles in every app that answers it.
- **Make the player a tile by default.** Omarchy floats it and pins it, and a player that opens over the page the person is reading should not move every tile.

## Consequences

`Super+Ctrl+F` does nothing in a browser built on Chromium or in an Electron app. From the CLI or the Menu it does nothing unless the focused window still has the keyboard.

An app that makes itself fullscreen just after `Super+Ctrl+F`, by any means, stays on its tile.

A window that drops its caption and takes more than one arrange to fill the monitor is held on its tile for that arrange, and the watcher keeps it there. Chromium, Windows Terminal, and Firefox change the style and the size in one step.

The person starts picture-in-picture in the browser. A browser page titled “Picture in picture” and nothing else is taken for a player by `Super+T`, and is kept above the windows.
