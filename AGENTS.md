# Winland

Winland is a Windows 11 window manager that matches the features and behavior of a pinned Hyprland release wherever Windows allows. It is not packaged as MSIX.

## Setup

Run `pwsh scripts/dev.ps1` once after cloning. It clones the pinned release (<https://github.com/hyprwm/Hyprland>, commit `e2d94f5`, after v0.56.2; ADR 0008) into `docs/agents/hyprland/`, fetches its wiki into `docs/agents/hyprland/site/`, and points git at the hooks in `scripts/hooks`. Read the reference behavior there.

## Domain

- `CONTEXT.md` is the glossary and `docs/adr/` holds decisions. See `docs/agents/domain.md`.
- Code, identifiers, file names and user-visible strings call the reference "the pinned release". The word "hyprland" never appears in them (ADR 0002).

## Workflow

- `CONTRIBUTING.md` covers branch names, commit messages, evidence, documentation, and how a pull request lands. Open a pull request only when the maintainer asks.
- Issues are GitHub Issues on `dante-sparras/winland`. An issue labelled `ready-for-agent` is specified well enough to fix without asking. See `docs/agents/issue-tracker.md` and `docs/agents/triage-labels.md`.
- `pwsh -NoProfile -File scripts/check.ps1` lints and builds. Run `dotnet test` on the test project that covers the change. CI runs `scripts/test.ps1`, and `scripts/mutate.ps1` is the release lane.
- The committed version is `0.0.0`. Releases are cut with `scripts/release.ps1`; see `docs/releasing.md`.

## Records

Each record has one home, and tests hold them in step:

- A Gap is a row in `docs/gaps.md` and an entry in `src/Winland.Core/ConfigGaps.cs`. A test checks that they agree.
- A Winland extension is in `docs/extensions.md`, and honored behavior is in `docs/behavior.md`.
- A change that makes a skipped conformance case pass removes its Skip and updates its row and the score in `docs/conformance.md`. A test checks that they agree.

## Running a build on this computer

`pwsh -NoProfile -File scripts/run.ps1` publishes this checkout, asks the running winland to exit (which puts every window back), and starts the new build in its place in about ten seconds. Use it for every start and stop, in place of `dotnet run`, `Stop-Process` or a hand-built `winlandctl` call.

- A running winland takes over every window on the desktop, including the user's. Start it only when the user asked to see a change, and stop it when you are done.
- To test without touching the user's windows, set `WINLAND_PIPE` and `WINLAND_STATE` to names of your own, then pass `-Config` a file that ignores every window (`windowrulev2 = ignore, class:.*`) and tiles only your test windows. With `WINLAND_PIPE` set, the script replaces only the copies it started. Rule patterns match the whole string, so `^foo` matches only `foo`.
- `-Stop` stops winland cleanly, which puts the taskbar back. A forced stop leaves the taskbar hidden; recover by running the script and then `-Stop`.
- `-Config <file>` reads that `.conf` or `.lua` file in place of `%APPDATA%\winland\winland.lua` or, without it, `winland.conf`.
- The build starts without administrator rights, so it cannot place the windows of elevated programs. `-Elevated` waits for a UAC approval, which an agent cannot give.
- Each build goes in its own folder under `%LOCALAPPDATA%\winland-dev`, with its output in `winland.out.log` and `winland.err.log`. A build that fails to publish leaves the running winland alone. Piping the script's output is safe.

## Notes

I like ambitious ideas, simple systems, and software that feels obvious. Do not preserve complexity just because it already exists. Do not introduce machinery because it looks architecturally impressive. Understand the real constraint, then fight for the smallest model that makes the correct behavior unsurprising.

Channel both "measure twice, cut once" and "yagni". Fight scope creep. Try to honor the dev's intent in both a minimal and realistic fashion.
