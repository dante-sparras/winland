# The chord ledger does not ask Windows whether Super is down

The hook's own key-downs and key-ups are the only account of Super. A swallowed Super never reaches Windows, so Windows reports that key up for the whole hold. Asking Windows whether Super is down either clears a chord the finger is still holding, or leaves the ledger swallowing every letter after a key-up was missed.

A Super down is sent to Windows only in the same run as its key-up, and only for a chord Winoma does not own. A bare tap sends nothing, so the Windows Start menu stays closed. A bound chord sends nothing. A session lock drops every finger and releases Super, and so does ending the process. Typing does not wait on a reboot.

`ChordLedger` is that policy. The hook reports a key and performs the result. A key-up reaches Windows only when its key-down did.

Windows is asked about a key in two cases, and neither clears a Super:

- A key Windows was given is one it can answer for, so the ledger asks about that key on the next key-down (ADR 0064).
- A Super that Windows already holds, because another hook handed it the press, is adopted as a finger.

## Considered options

- **A timer that looks for a stuck key.** It would drop a Super finger with no key-up after a while, release a modifier Windows still reports down, and forget a delivered key Windows reports up. Two of those checks send keys, and the desktop works without them.
