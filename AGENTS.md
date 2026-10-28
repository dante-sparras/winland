# Winland

A keyboard-driven tiling window manager for Windows 11. It manages windows on top of Explorer and DWM, and is not packaged as MSIX.

## Stack

- C# on .NET 10 (SDK 10.0.401, pinned in `global.json`), Windows only
- xunit tests, and Stryker.NET 5.0.0 for mutation testing
- PowerShell 7 for the scripts in `scripts/`
- GitHub Actions on Windows runners, and release-please for releases

## Project Structure

```text
.
├── src/
│   ├── Winland.Domain/          # The model and the config language, with no Win32
│   ├── Winland.Application/     # WindowManager, the command protocol and the event stream
│   ├── Winland.Infrastructure/  # Win32: hooks, window placement, monitors, tray, IPC pipe
│   ├── Winland.Daemon/          # winland.exe: reads the config and wires the rest together
│   └── Winland.Install/         # Install, update and release files
├── tests/                       # One *.Tests project per src project, plus smoke tests
├── scripts/                     # check, test, run, smoke, mutate, install, and the git hooks
├── docs/
│   ├── adr/                     # Decision records
│   ├── architecture.md          # The layers, the path an input takes, where to change things
│   ├── behavior.md              # How Winland behaves, every Option and Dispatcher
│   ├── development.md           # Setup, environment, and every command
│   ├── releasing.md             # How release-please cuts a release
│   └── agents/                  # How the engineering skills use this repo
├── GLOSSARY.md                  # The domain glossary
├── CONTRIBUTING.md              # Branches, commit and PR titles, PR rules
└── .agents/skills/              # Agent skills; a copy in .claude/skills/
```

## Before you finish

- Run `pwsh -NoProfile -File scripts/check.ps1`, then `pwsh -NoProfile -File scripts/test.ps1`. Everything must pass. While iterating, `dotnet test` on the test project that covers the change is enough.
- Name the branch, title the PR, and fill in the PR template as [CONTRIBUTING.md](CONTRIBUTING.md#open-a-pr) describes. Open a PR only when the maintainer asks.
- Say what you couldn't verify.
- Added or changed a command, prerequisite, environment variable, or top-level folder? Update [docs/development.md](docs/development.md) or Project Structure.
- Changed how Winland behaves? Update `docs/behavior.md`. A standing decision is an ADR in `docs/adr/`, kept in step with `docs/adr/README.md`.
- Don't edit `.agents/skills/` or `.claude/skills/`; the `skills` CLI overwrites them.
- Never commit `.env` files or credentials.

## Conventions

- Option and Dispatcher names are Winland's, documented in `docs/behavior.md` (ADR 0002). An unknown keyword warns and is skipped.
- Don't commit implementation plans, research notes, or screenshots that exist only for a PR. Don't add a file catalog or a narration of control flow to the docs.

### Running a build on this computer

Before starting, stopping or isolating a dev build, read [Run a dev build](docs/development.md#run-a-dev-build) for the commands, config, environment, logs and recovery steps.

- A running Winland takes over the user's windows. Start it only when the user asked to see a change, and stop it when you are done.
- Use `scripts/run.ps1` for every start and stop. For isolated tests, give it your own `WINLAND_PIPE`, `WINLAND_STATE` and a `-Config` that ignores every window except your test windows.
- `-Elevated` waits for UAC approval, which an agent cannot give.

## Agent skills

### Issue tracker

GitHub Issues on dante-sparras/winland, through `gh`. An issue labelled `ready-for-agent` is specified well enough to fix without asking. See `docs/agents/issue-tracker.md`.

### Triage labels

The five default roles, each label named after its role. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` at the root and `docs/adr/`. See `docs/agents/domain.md`.

## Notes

I like ambitious ideas, simple systems, and software that feels obvious. Do not preserve complexity just because it already exists. Do not introduce machinery because it looks architecturally impressive. Understand the real constraint, then fight for the smallest model that makes the correct behavior unsurprising.

Channel both "measure twice, cut once" and "yagni". Fight scope creep. Try to honor the dev's intent in both a minimal and realistic fashion.
