# Winland

Winland is the Windows 11 window manager that matches Hyprland's features and behavior

## Setup

Run `pwsh dev.ps1` after cloning, before reading Hyprland. It clones Hyprland into `docs/agents/hyprland/` and fetches its wiki into `site/` there. Both are gitignored.

## Stack

C# on .NET 10. Nullable reference types and implicit usings are on. Warnings are errors. Not packaged as MSIX.

A version is `0.1.<commits on HEAD>`, printed by `version.ps1`.

## Issues

Issues are GitHub Issues on this repo, labelled with the roles in `docs/agents/triage-labels.md`. An issue labelled `ready-for-agent` is specified well enough for an agent to fix without asking.

## More

- Hyprland Repo - `docs/agents/hyprland/` (https://github.com/hyprwm/Hyprland, branch main).
- Hyprland Docs - `docs/agents/hyprland/site/`
