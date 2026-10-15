# Winland

Winland is the Windows 11 window manager that matches Hyprland's features and behavior

## Setup

Run `pwsh scripts/dev.ps1` after cloning, before reading Hyprland. It clones Hyprland into `docs/agents/hyprland/` and fetches its wiki into `site/` there. Both are gitignored. It also installs the pre-push lint hook.

Branch names, commit messages, and how a pull request lands are in `CONTRIBUTING.md`.

## Stack

C# on .NET 10. Nullable reference types and implicit usings are on. Warnings are errors. Not packaged as MSIX.

A version is `0.1.<commits on HEAD>`, printed by `version.ps1`.

## Issues

Issues are GitHub Issues on this repo, labelled with the roles in `docs/agents/triage-labels.md`. An issue labelled `ready-for-agent` is specified well enough for an agent to fix without asking.

## Agent skills

### Issue tracker

Issues are GitHub Issues on `dante-sparras/winland`. See `docs/agents/issue-tracker.md`.

### Triage labels

The five default roles (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`), each label string equal to its role name. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` is the glossary and `docs/adr/` holds decisions. See `docs/agents/domain.md`.

## More

- Hyprland Repo - `docs/agents/hyprland/` (https://github.com/hyprwm/Hyprland, tag v0.56.2).
- Hyprland Docs - `docs/agents/hyprland/site/`
