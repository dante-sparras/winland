# The pointer shows only while the mouse is used

A keyboard move never shows the pointer. Focusing a window with `Super+Arrow`, a group chord, or `winoma hyprland focus app` leaves the pointer where the person left it. Winoma does not warp it to the focused window, nor to the monitor the keyboard goes to (ADR 0079). Hyprland does both. Leaving the pointer is a decision, not a Gap.

The pointer is drawn while the mouse or touchpad is in use. A key hides it at once, and a still pointer hides after `pointerHideSeconds` in `settings.json`, 2 by default. Zero never hides it. A move, a press, or a scroll shows it again. A held button keeps it on screen, and so does the lock screen or an elevation prompt, where Winoma cannot hear the mouse. A program that sets the cursor position does not show it.

Hiding swaps every system cursor for a blank one, and showing reloads the person's cursors. A program that draws its own cursor keeps it. A desktop that dies cannot reload them, so the desktop watcher reloads them when the desktop exits, and startup reloads them too. The watcher is the same program under the name `Winshell.Watch.exe`, which the build copies beside `Winshell.Host.exe`. Stopping every `Winshell.Host` process, as replacing the desktop does, leaves the watcher alive to reload them.

Pointer input comes from the low-level mouse hook that already guards the caption buttons. A precision touchpad reports its events as injected, so injected events count. The hook does not hear the pointer over an elevated window, so a move Windows counted as input also shows it.

A hidden pointer comes back on a signal: the mouse hook's, or the cursor event Windows raises when the pointer moves on screen. Windows raises that event for the thread whose window is under the pointer, so the watch listens to Winoma's own process as well as to every other. Over the bar or a panel the cursor event is Winoma's.

The key that hides the pointer is one a keyboard hook heard from a keyboard. The desktop hook stamps the time of each physical key, and the elevated hook stamps the ones the desktop hook did not hear. Windows' own last-input time is not used for this: it moves with input that neither hook hears, and reading that as a key hides the pointer a fraction of a second after the mouse stops. A key another program types does not hide the pointer, and neither does a key in an administrator window while `administratorWindows` is off. The still wait hides it in both cases.

Hover focus waits for the pointer's own window. A window drawn over the tile under the pointer, such as the snipping overlay, a held button, or an open panel keeps the keyboard where it is.

Hover focus runs only when the pointer moved, and a key puts it on hold until the pointer moves again. So a pointer that rests on one monitor takes no keyboard back from the monitor a Chord sent it to (ADR 0079). Once the mouse moves, the tile under it takes the keyboard, on whichever monitor it is. The bare desktop of another monitor takes the keyboard to that monitor, and so does a left press there.
