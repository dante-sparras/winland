# Winland

Winland is the Windows 11 window manager that matches Hyprland's behavior: Tiling, Layout, Workspaces, and every bind. It runs alone, and Winshell and Winoma run on it. Use the terms in `CONTEXT.md`. `docs/adr/README.md` lists every decision by area: read it before changing how an area behaves, then the ADRs it names. The CLI is `docs/user/cli.md`, and the settings are `docs/user/config.md`.

Winland, Winshell (https://github.com/dante-sparras/winshell), and Winoma (https://github.com/dante-sparras/winoma) are three repos (Winoma ADR 0177). This repo holds only Winland.

## Setup

Run `pwsh dev.ps1` after cloning, before reading Hyprland. It clones Hyprland into `docs/agents/hyprland/` and fetches its wiki into `site/` there. Both are gitignored.

## Commands

Prepend the Visual Studio installer to `PATH` in every PowerShell that builds or publishes. NativeAOT finds `vswhere.exe` there.

```powershell
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;" + $env:PATH
dotnet test tests/Winland.Tests/Winland.Tests.csproj
dotnet build src/Winland/Winland.csproj
dotnet build src/Winland.Cli/Winland.Cli.csproj
pwsh publish.ps1
pwsh pack.ps1
```

`publish.ps1` writes `Winland.Host.exe`, `Winland.Watch.exe`, the native `winland.exe`, and `version.json` to `%TEMP%\winland-publish\Winland`, and throws when the folder is not whole. Winland runs from a copy in `%LOCALAPPDATA%\Winland`.

`pack.ps1` packs Winland.Core, Winland.Ipc, and Winland.Platform into `%USERPROFILE%\.nuget\local-feed`, and prints the version. Winshell's and Winoma's `dev.ps1 -Local` build against that feed, so a fix here can be tried there before it is released.

`winland workspaces`, `winland clients`, `winland activewindow`, and `winland dispatch <command> [args]` ask the running window manager. `-j` prints JSON.

## Stack

C# on .NET 10. Nullable reference types and implicit usings are on. Warnings are errors. Not packaged as MSIX.

| Project | Holds |
| --- | --- |
| `src/Winland.Ipc` | The JSON-line contract between Winland and the shell: the two pipes, the handshake, pushed state, queries, and events. References nothing. A package |
| `src/Winland.Core` | Window-manager behavior that runs without Windows: Tiling, Workspaces, binds, the hidden-window record, chord text, and the window-property marks. References nothing. A package |
| `src/Winland.Platform` | The window manager's Win32: the keyboard hook, the placer, window properties, and the elevated helper. References only Winland.Core. A package |
| `src/Winland` | `Winland.Host.exe`, the window manager's process. References only Winland.Core, Winland.Platform, and Winland.Ipc |
| `src/Winland.Cli` | The `winland` client. References only Winland.Ipc |
| `tests/Winland.Tests` | Winland.Core, Winland.Ipc, and the `winland` argument parser |

Public Core members carry XML documentation. Tests use MSTest and FluentAssertions, one test at a time.

## Versions and packages

A version is `0.1.<commits on HEAD>`, printed by `version.ps1`. Each green build of main pushes the three packages to GitHub Packages and makes the release `v<version>` with `Winland-win-x64.zip`. Winshell and Winoma pin a version. A change to a public member of a package, or to a message in Winland.Ipc, breaks them when they move to that version, so say so in the commit.

The handshake stamp is a hash of the Winland.Ipc contract (Winoma ADR 0176). A Winshell built against another Winland.Ipc is refused. Winoma installs the Winland and Winshell versions it pins, which Winshell's release says speak to each other.

## Boundaries

- Win32 lives in `src/Winland.Platform`. A new behavior gets a Core type and a test before a platform call.
- Start `Winland.Host.exe` through WMI, as below. A child of the agent command dies with the command. Leave `Winland.Watch` running. It runs from a copy outside the live folder and exits when `Local\Winland` is held.
- A file in `.config\winland` may `include` others. Included files merge key by key: objects merge, arrays and scalars replace, and `null` removes a key. The file that includes them applies last. Binds merge one chord at a time, and `"command": null` removes that chord.
- Winland is unreleased, and Dante's Host is the only install. Rename an id or change a file format outright, and fix his files in `%USERPROFILE%\.config\winland` in the same change. A change Winshell or Winoma must follow is an issue in that repo.

## Workflow

After a change:

1. `dotnet test tests/Winland.Tests/Winland.Tests.csproj` passes, and `dotnet build src/Winland/Winland.csproj` and `dotnet build src/Winland.Cli/Winland.Cli.csproj` have no warnings.
2. Replace Winland. Publish while the old one runs, and go on only when the publish succeeded. Copy `%USERPROFILE%\.config\winland` to a timestamped folder under `%LOCALAPPDATA%` before the swap. Leave `Winshell.Host` and `Winshell.Watch` running.

   ```powershell
   $next = Join-Path $env:TEMP 'winland-publish\Winland'
   $live = Join-Path $env:LOCALAPPDATA 'Winland'
   Remove-Item (Join-Path $live '*.old') -ErrorAction SilentlyContinue
   Set-Content (Join-Path $env:USERPROFILE '.config\winland\lifetime') replacing -NoNewline
   Get-Process Winland.Host -ErrorAction SilentlyContinue | Stop-Process -Force
   $gone = (Get-Date).AddSeconds(30)
   while ((Get-Date) -lt $gone -and (Get-Process Winland.Host -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 200 }
   if (Get-Process Winland.Host -ErrorAction SilentlyContinue) { throw 'the old Winland is still running' }
   New-Item -ItemType Directory -Force $live | Out-Null
   Get-ChildItem $live -File | Where-Object { $_.Name -like 'Win*' } | Rename-Item -NewName { $_.Name + '.old' }
   robocopy $next $live /E /XD watch /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
   if ($LASTEXITCODE -ge 8) { throw 'the copy of Winland failed' }
   Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "`"$live\Winland.Host.exe`""; CurrentDirectory = $live } | Out-Null
   ```

   `replacing` tells `Winland.Watch` that a build follows (ADR 0059). The new Winland reads it and takes the hidden windows over still hidden. If `Local\Winland` is not held 30 seconds after the old process exits, `Winland.Watch` shows the windows. A process that starts while a killed one still holds `Local\Winland` exits at once, so the wait comes first.

   Wait up to 60 seconds for `ready` in `winland.log`. Roll back when the process exits before `ready`: restore the config backup and the `.old` files, and start `Winland.Host.exe`. Done when `Winland.Host` is alive and `winland.log` has `ready`.
3. Check it with `winland workspaces`, `winland clients`, and `winland dispatch`.

Diagnose in this order, and leave the desktop alone until the bug has a name: `winland clients` and `winland workspaces`, then `winland.log` for that pid, then one `winland dispatch` to reproduce, then the screen. Read `docs/agents/files.md` before reading or writing `hidden`, `lifetime`, `winland-restarts`, or `winland.log`.

## Issues

Issues are GitHub Issues on this repo, labelled with the roles in `docs/agents/triage-labels.md`. A bug found here that belongs to Winshell or Winoma is an issue there: `gh issue create -R dante-sparras/winshell` or `-R dante-sparras/winoma`, with what was run, what was seen, and the log lines. An issue labelled `ready-for-agent` is specified well enough for an agent to fix without asking.

## More

Hyprland is cloned at `docs/agents/hyprland/` (https://github.com/hyprwm/Hyprland, branch main). Read `site/` there before matching a Hyprland behavior.
