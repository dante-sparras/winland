# Winland

A tiling window manager for Windows 11 that matches Hyprland's behavior: dwindle and scrolling layouts, Workspaces 1 to 9 on every monitor, a Scratchpad, Groups, and every bind in one `binds.json`. It draws no bar. [Winshell](https://github.com/dante-sparras/winshell) draws one on it, and [Winoma](https://github.com/dante-sparras/winoma) makes both into Omarchy's desktop.

- `Winland.Host.exe` is the window manager. `winland` asks it, the way `hyprctl` asks Hyprland: [docs/user/cli.md](docs/user/cli.md).
- Settings and binds are in `%USERPROFILE%\.config\winland`: [docs/user/config.md](docs/user/config.md).
- Decisions are in [docs/adr](docs/adr/README.md), and the words in [CONTEXT.md](CONTEXT.md).

Winoma's install puts Winland in `%LOCALAPPDATA%\Winland`. Each release here is `Winland-win-x64.zip`, and Winland.Core, Winland.Ipc, and Winland.Platform are packages on GitHub Packages.
