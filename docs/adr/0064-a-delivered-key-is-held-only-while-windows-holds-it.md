# A delivered key is held only while Windows holds it

The hook is not called while an administrator window is in front. Ctrl+Shift+Esc brings Task Manager forward with all three keys down, so their key-ups never reach the hook. A ledger that kept Ctrl and Shift as fingers would resolve every later Chord as Super+Ctrl+Shift and its key, and no Chord would run until each of those keys was pressed again or the session locked.

A key Windows was given is one Windows can answer for: its key-up reaches Windows whether or not the hook is called. On a new key-down the ledger asks Windows about each such finger and forgets the ones Windows reports up. That is the moment the answer is exact and the moment it matters. The hook is being called again, Windows has finished every earlier key, and the fingers are about to be read as a chord's modifiers. Nothing is polled and nothing sends a key, which is what ADR 0017 turned down.

A held Super is never asked about, and neither is a key the ledger swallowed. Windows never had them, so its answer proves nothing (ADR 0017). A Super whose key-up is missed keeps the paths it had: raw input, the elevated helper, and the session lock. A swallowed chord key whose key-up is missed stays a finger until that key is pressed again.

The cause is not looked for. A key-up lost to an administrator window, to a consent prompt, or to a program that took the key after the hook passed it is the same finding, and so is one lost a way nobody has met yet.

Hover-focus reads the same account. `PointerYield` keeps no count and the hook keeps no set of held keys. A pointer move asks the ledger whether a key is held, and the ledger asks Windows about the delivered ones without forgetting any, so it can be asked between keys.

The hook sends the keys that waited through a Workspace switch as soon as the ledger hands them over. The ledger records such a key-down as given to Windows at that moment and asks Windows about it on the next key-down, so the key has to be there by then.

The elevated helper's ledger asks nothing. The Super it adopts from the desktop hook is one Windows never had, and it abandons every finger when the desktop hook has the keyboard again.
