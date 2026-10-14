# Super+L changes Layout, not the lock screen

`Super+L` toggles the current Workspace between dwindle and scrolling. Omarchy Quattro locks with `Super+Ctrl+L`, so Winoma does too. Each Host turns off Windows' own lock binding on `Super+L`. Locking the PC is still one chord, `Super+Ctrl+L`.

The policy that turns off the `Super+L` binding, `DisableLockWorkstation`, also makes Windows refuse `LockWorkStation`. So a lock lifts the policy first, and Winoma sets it again when the session comes back, or after ten seconds when the lock never came.
