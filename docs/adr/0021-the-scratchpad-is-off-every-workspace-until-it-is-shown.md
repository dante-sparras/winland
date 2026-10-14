# The Scratchpad is off every Workspace until it is shown

The Scratchpad is on no Workspace. Its windows are off the screen until `Super+S` or `Super+grave` shows them over the Workspace with the keyboard, on that Workspace's monitor. `Super+Alt+S` and `Super+Shift+grave` move the focused window there. Winoma hides them the one way it hides any window: Explorer's cloak, or `SW_HIDE` where the cloak is not used (ADR 0059). A window in the Scratchpad carries the Scratchpad's mark, so it is still there after the desktop is replaced (ADR 0078).

A cloaked window keeps the keyboard. So before a Scratchpad window that has the keyboard is hidden, the keyboard goes to the Workspace's focused window, or to no window when the Workspace has none.

Because the Scratchpad is on no Workspace, showing it is the same wherever it was last shown: its windows are shown, and placed over the tiles of the Workspace with the keyboard. There is one Scratchpad for every monitor (ADR 0079). Its Chord while it is shown on another monitor takes it from there to the monitor with the keyboard, in one press.

A shown Scratchpad is hidden when another Workspace comes in front on the monitor it is shown on. Its windows are hidden with the windows of the Workspace that is left, after the keyboard has moved (ADR 0003). A keyboard that only goes to another monitor leaves it shown, since the Workspace it is over is still in front. Omarchy sets `hide_special_on_workspace_change` for the same reason: left shown, it reads as open on a Workspace where nothing of it is drawn, and `Super+S` takes two presses. A jump to the Workspace already in front leaves it shown.

A hidden Scratchpad window that takes the keyboard by itself shows the Scratchpad (ADR 0038). A window that cannot be hidden does not stay in the Scratchpad: it is on the glass, so it takes a tile on the Workspace in front (ADR 0003).
