# The keyboard hook is installed once

The keyboard hook is installed once, and `HookRepair` installs it again only on evidence: raw input reports keys Windows received that the hook was never called for, a call starts long after its key, a Super goes past the hook, or the session unlocks. Installs close together wait, and a repeated fight with another hook backs off. A hook that is gone does not wait out the backoff.

## Considered options

- **Install it again every few milliseconds, so it is always called first.** That fights every other hook on the machine for first place, and it does not notice when Windows removes the hook after a missed timeout.
- **Install it again when the elevated helper hooks in after it.** Chords work over administrator windows without that.
