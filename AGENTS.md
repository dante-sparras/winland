# Project instructions

Run `pwsh scripts/dev.ps1` after cloning, before reading Hyprland. It clones Hyprland into `docs/agents/hyprland/` and fetches its wiki into `site/` there. It also points git at the tracked hooks in `scripts/hooks` (the pre-push lint hook and the post-merge cleanup).

Issues are GitHub Issues on `dante-sparras/winland`, labelled with the roles in `docs/agents/triage-labels.md`. An issue labelled `ready-for-agent` is specified well enough for an agent to fix without asking. See `docs/agents/issue-tracker.md`.

# Conventions

- Branch names, commit messages, pull-request approval, evidence, and how a pull request lands are in `CONTRIBUTING.md`. Open a pull request only when the maintainer asks.
- C# on .NET 10. Nullable reference types and implicit usings are on. Warnings are errors.
- Commands: `pwsh -NoProfile -File scripts/check.ps1` lints and builds. `dotnet test` the test project that covers the change. CI runs `scripts/test.ps1`. `scripts/mutate.ps1` is the release lane.
- Update a document when this change makes it false, and edit it in place. When to leave a document alone is in `CONTRIBUTING.md`.
- The committed version is `0.0.0`. Releases are cut with `scripts/release.ps1`. See `docs/releasing.md`.
- Triage labels are the five default roles (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`), each label string equal to its role name.
- The Hyprland checkout and its wiki are gitignored.
- The word "hyprland" never appears in code, identifiers, file names or user-visible strings (ADR 0002).
- Every Gap is recorded in `docs/gaps.md`, Winland extensions in `docs/extensions.md`, and honored behavior in `docs/behavior.md`.
- A change that makes a skipped conformance case pass removes its Skip and updates its row and the score in `docs/conformance.md`. A test checks that they agree.

# Running a build on this computer

`pwsh -NoProfile -File scripts/run.ps1` publishes this checkout, asks the running winland to exit, which puts every window back, and starts the new build in its place. It takes about ten seconds. Use it instead of `dotnet run`, `Stop-Process` or a hand-built `winlandctl` call.

- It starts the build without administrator rights, so no UAC prompt appears and nobody has to click. That build cannot place the windows of elevated programs. `-Elevated` starts it elevated, as the logon task does, and waits for one UAC approval, which an agent cannot give.
- A build that fails to publish leaves the running winland alone. Each build goes in its own folder under `%LOCALAPPDATA%\winland-dev`, so a running one never locks the next. Its output is in `winland.out.log` and `winland.err.log` there.
- `-Stop` stops winland and starts nothing. `-Config <file>` reads that file instead of `%APPDATA%\winland\winland.conf`.
- Never stop winland by force. It hides the taskbar while it runs and puts it back only when it exits cleanly. After a forced stop, run the script and then `-Stop`: the start puts the taskbar back.
- A running winland takes over every window on the desktop, the ones the user is working in too. Start it only when the user asked to see a change, and stop it when you are done. To try a build without touching their windows, pass a config that ignores every window (`windowrulev2 = ignore, class:.*`) and then tiles only your own test windows, and set `WINLAND_PIPE` and `WINLAND_STATE` to names of your own first. With `WINLAND_PIPE` set the script replaces only the copies it started.
- Piping the script's output is safe. The shell starts winland, so it holds none of the script's handles.

# Architecture

Winland is the Windows 11 window manager that matches Hyprland's features and behavior. It is not packaged as MSIX.

- Domain docs are single-context: `CONTEXT.md` is the glossary and `docs/adr/` holds decisions. See `docs/agents/domain.md`.
- Hyprland repo: `docs/agents/hyprland/` (<https://github.com/hyprwm/Hyprland>, tag v0.56.2).
- Hyprland docs: `docs/agents/hyprland/site/`.
