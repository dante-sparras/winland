# Decisions

One line per ADR, by area. Read the ADRs for an area before changing how it behaves.

An ADR states one standing decision and why, in the present tense, and is true against the code. When a decision changes, rewrite its ADR in place and fix its line here. Git keeps the history. A new ADR takes the next number after the highest already in this repo.

An ADR without a Status line is accepted and true against the code. `Status: Proposed` marks a decision that is not built yet and points at the issue that builds it, and the index marks it `(proposed)`. A Proposed ADR is not evidence of how Winland behaves. Remove the Status line when the work lands.

## Architecture

- [0001](0001-winland-runs-on-top-of-explorer-and-dwm.md): a background process on Explorer and DWM, no shell replacement, no injection.
- [0003](0003-workspaces-are-owned-by-winland.md): Workspaces are Winland's own, not Windows virtual desktops, and each Monitor has its own 1 to 10.
- [0004](0004-winland-runs-elevated.md): Winland runs elevated by default, to control elevated windows, and can run as the normal user.
- [0005](0005-pure-core-and-windows-adapter.md): a pure core with no Win32, a thin Windows adapter, and `winlandctl` as its own project.
- [0006](0006-startup-through-a-scheduled-task.md): `winland install` copies itself under Program Files and registers an elevated logon task; `--user` uses the Run key, unelevated.
- [0007](0007-elevation-does-not-leak-to-launched-programs-or-ipc.md): launched programs are always de-elevated, the pipe is limited to the current user, and the config is an ordinary user file.
- [0014](0014-winland-manages-windows-and-leaves-the-desktop-to-windows.md): Winland manages windows and leaves settings, notifications, effects and the taskbar to Windows. The installer may turn off Snap and touchpad swipes, with consent.
- [0015](0015-install-and-update-from-published-releases.md): one PowerShell line installs the newest release, and `winland update` follows the stable or the dev channel. (proposed)

## Config and names

- [0002](0002-hyprland-compatibility-without-its-name.md): Winland keeps the option and Dispatcher names it started with; they are its own now.
- [0008](0008-the-spec-is-a-pinned-hyprland-release.md): Winland's own `docs/behavior.md` is the spec; Hyprland is its origin, not its reference.
- [0010](0010-unsupported-keywords-warn-and-are-ignored.md): unknown and malformed lines warn and are skipped, and the rest of the config loads.
- [0013](0013-config-is-line-based-or-lua.md): one line-based `winland.conf` in `%USERPROFILE%\.winland` that runs no code, written with defaults on first start.

## Extending

- [0011](0011-plugins-are-in-process-and-hash-approved.md): no plugins; scripts extend Winland over the IPC pipe as the normal user.

## Reliability

- [0009](0009-hidden-clients-survive-a-crash.md): hidden Clients are recorded and recovered after a crash.
