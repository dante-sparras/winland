# Winland

Winland is the Windows 11 window manager that matches Hyprland's features and behavior

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

## Stack

C# on .NET 10. Nullable reference types and implicit usings are on. Warnings are errors. Not packaged as MSIX.

Public Core members carry XML documentation. Tests use MSTest and FluentAssertions, one test at a time.

## Versions and packages

A version is `0.1.<commits on HEAD>`, printed by `version.ps1`. Each green build of main pushes the three packages to GitHub Packages and makes the release `v<version>` with `Winland-win-x64.zip`.

## Issues

Issues are GitHub Issues on this repo, labelled with the roles in `docs/agents/triage-labels.md`. An issue labelled `ready-for-agent` is specified well enough for an agent to fix without asking.

## More

- Hyprland Repo - `docs/agents/hyprland/` (https://github.com/hyprwm/Hyprland, branch main).
- Hyprland Docs - `docs/agents/hyprland/site/`
