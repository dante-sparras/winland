# A focus does not join thread input while Super is held

Joining another thread's input copies the keys that thread believes are down. A workspace Chord focuses a window while Super is still held. The hook then swallows Super's key-up, so the joined thread keeps Super and letters stop arriving until Super is pressed again.

A focus may join only while the chord ledger records no Super finger. The elevated helper never joins: it is already allowed to take an administrator window, and joining Task Manager is what sticks Super. Windows is still not asked whether Super is down (ADR 0017).

A Workspace switch is such a focus, and it also has windows to hide (ADR 0003). Two things follow while Super is held:

- A window that refused the keyboard keeps waiting for it. When Super goes up, the Workspace's focused window is asked once more, now with the join.
- The keyboard is not dropped to the desktop's own window either. Giving Explorer the keyboard while Super is down leaves the later keys as Windows chords, because the hook swallows Super's key-up. So after a switch to an empty Workspace the keyboard stays on the window that was just hidden until Super goes up. The keyboard hook starts no wait for that window in the meantime (ADR 0059).
