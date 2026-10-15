# Project instructions

Run `pwsh scripts/dev.ps1` after cloning, before reading Hyprland. It clones Hyprland into `docs/agents/hyprland/` and fetches its wiki into `site/` there. It also installs the pre-push lint hook.

Issues are GitHub Issues on `dante-sparras/winland`, labelled with the roles in `docs/agents/triage-labels.md`. An issue labelled `ready-for-agent` is specified well enough for an agent to fix without asking. See `docs/agents/issue-tracker.md`.

# Conventions

- Branch names, commit messages, and how a pull request lands are in `CONTRIBUTING.md`.
- C# on .NET 10. Nullable reference types and implicit usings are on. Warnings are errors.
- A version is `0.1.<commits on HEAD>`, printed by `version.ps1`.
- Triage labels are the five default roles (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`), each label string equal to its role name.
- The Hyprland checkout and its wiki are gitignored.

# Architecture

Winland is the Windows 11 window manager that matches Hyprland's features and behavior. It is not packaged as MSIX.

- Domain docs are single-context: `CONTEXT.md` is the glossary and `docs/adr/` holds decisions. See `docs/agents/domain.md`.
- Hyprland repo: `docs/agents/hyprland/` (<https://github.com/hyprwm/Hyprland>, tag v0.56.2).
- Hyprland docs: `docs/agents/hyprland/site/`.
