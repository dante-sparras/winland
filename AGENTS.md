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

`pwsh -NoProfile -File scripts/run.ps1` publishes this checkout, asks the running winland to exit (which puts every window back), and starts the new build in its place in about ten seconds. Use it for every start and stop, in place of `dotnet run`, `Stop-Process` or a hand-built `winland dispatch` call.

- A running winland takes over every window on the desktop, including the user's. Start it only when the user asked to see a change, and stop it when you are done.
- To test without touching the user's windows, set `WINLAND_PIPE` and `WINLAND_STATE` to names of your own, then pass `-Config` a file that ignores every window (`windowrule = ignore, class:.*`) and tiles only your test windows. With `WINLAND_PIPE` set, the script replaces only the copies it started. Rule patterns match the whole string, so `^foo` matches only `foo`.
- `-Stop` stops winland cleanly. When `general:hide_taskbar` hid the taskbar, that shows it again. A forced stop leaves it hidden; recover by running the script and then `-Stop`.
- `-Config <file>` reads that `.conf` file in place of `%USERPROFILE%\.winland\winland.conf`.
- The build starts without administrator rights, so it cannot place the windows of elevated programs. `-Elevated` waits for a UAC approval, which an agent cannot give.
- Each build goes in its own folder under `%LOCALAPPDATA%\winland-dev`, with its output in `winland.out.log` and `winland.err.log`. A build that fails to publish leaves the running winland alone. Piping the script's output is safe.

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
