# Decisions

One line per ADR, by area. Read the ADRs for an area before changing how it behaves.

An ADR states one standing decision and why, in the present tense, and is true against the code. When a decision changes, rewrite its ADR in place and fix its line here. Git keeps the history. A new ADR takes the next number after the highest already in this repo.

An ADR without a Status line is accepted and true against the code. `Status: Proposed` marks a decision that is not built yet and points at the issue that builds it, and the index marks it `(proposed)`. A Proposed ADR is not evidence of how Winland behaves. Remove the Status line when the work lands.

## Architecture

- [0001](0001-winland-runs-on-top-of-explorer-and-dwm.md): a background process on Explorer and DWM, no shell replacement, no injection.
- [0003](0003-workspaces-are-owned-by-winland.md): Workspaces are Winland's own, not Windows virtual desktops, and each Monitor has its own 1 to 10.
- [0004](0004-winland-runs-elevated.md): Winland runs elevated by default to control elevated windows, while standard-user manual starts stay unelevated.
- [0005](0005-clean-architecture-in-four-layers.md): four layers, Domain, Application, Infrastructure and presentation, each referencing only the layers inside it.
- [0006](0006-startup-through-a-scheduled-task.md): the elevated task starts a native launcher under Program Files that clears runtime injection variables. User maintenance stays unelevated.
- [0007](0007-elevation-does-not-leak-to-launched-programs-or-ipc.md): launches and user-file access use a checked shell token, the pipe is limited to the current user, and key relays refuse elevated destinations.
- [0014](0014-winland-manages-windows-and-leaves-the-desktop-to-windows.md): Winland manages windows and leaves settings, notifications, effects and the taskbar to Windows. The installer may turn off Snap and touchpad swipes, with consent.
- [0015](0015-install-and-update-from-published-releases.md): one PowerShell line installs a verified release from protected staging with consent for Windows settings changes. Updates follow stable or dev.
- [0016](0016-per-window-state-stays-with-the-code-that-uses-it.md): what Winland knows about a window stays in the module that uses it, with no single record per Client; a guard test checks that closing a window leaves nothing behind.

## Config and names

- [0002](0002-hyprland-compatibility-without-its-name.md): Winland keeps the option and Dispatcher names it started with; they are its own now.
- [0008](0008-winland-keeps-what-windows-users-need.md): `docs/behavior.md` is the spec; a thing stays only when the starter config uses it or a Windows user would ask for it, and a removed keyword is deleted outright.
- [0010](0010-unsupported-keywords-warn-and-are-ignored.md): unknown and malformed lines warn and are skipped, and the rest of the config loads.
- [0013](0013-the-config-is-one-line-based-file-that-runs-no-code.md): one line-based `winland.conf` that runs no code and uses the shell user's file rights. Missing or empty default files get defaults.

## Extending

- [0011](0011-winland-is-extended-through-its-pipe-not-plugins.md): no plugins; scripts extend Winland over the IPC pipe as the normal user.

## Reliability

- [0009](0009-hidden-clients-survive-a-crash.md): hidden Clients are recorded and recovered after a crash.
