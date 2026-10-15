# Decisions

One line per ADR, by area. Read the ADRs for an area before changing how it behaves.

An ADR states one standing decision and why, in the present tense, and is true against the code. When a decision changes, rewrite its ADR in place and fix its line here. Git keeps the history. A new ADR takes the next number after the highest already in this repo.

An ADR without a Status line is accepted and true against the code. `Status: Proposed` marks a decision that is not built yet and points at the issue that builds it, and the index marks it `(proposed)`. A Proposed ADR is not evidence of how Winland behaves. Remove the Status line when the work lands.

## Architecture

- [0001](0001-winland-runs-on-top-of-explorer-and-dwm.md): a background process on Explorer and DWM, no shell replacement, no injection.
- [0003](0003-workspaces-are-owned-by-winland.md): Workspaces are Winland's own, not Windows virtual desktops.
- [0004](0004-winland-runs-elevated.md): Winland always runs elevated, to control elevated windows.
- [0005](0005-pure-core-and-windows-adapter.md): a pure core with no Win32, a thin Windows adapter, and `winlandctl` as its own project.
- [0006](0006-startup-through-a-scheduled-task.md): started at logon by an elevated scheduled task, not a service or Run key.
- [0007](0007-elevation-does-not-leak-to-launched-programs-or-ipc.md): launched programs are de-elevated, and the pipe is limited to the current user.

## Compatibility

- [0002](0002-hyprland-compatibility-without-its-name.md): Hyprland's names and semantics, Gaps documented, and "hyprland" never appears in the code.
- [0008](0008-the-spec-is-a-pinned-hyprland-release.md): the spec is one pinned Hyprland release tag.
- [0010](0010-unsupported-keywords-warn-and-are-ignored.md): unsupported keywords warn and are ignored, and the config still loads.
- [0013](0013-config-is-hyprlang-first-lua-later.md): the line-based config syntax first, Lua later.

## Plugins

- [0011](0011-plugins-are-in-process-and-hash-approved.md): in-process .NET plugins, approved by hash, with fault containment and an exact API version.
- [0012](0012-plugin-manager-builds-de-elevated-and-approves-through-uac.md): the plugin manager builds de-elevated and approves each change through one UAC confirmation.

## Reliability

- [0009](0009-hidden-clients-survive-a-crash.md): hidden Clients are recorded and recovered after a crash.
