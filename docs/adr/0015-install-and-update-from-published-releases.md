# Winland is installed by one command and updated by `winland update`

Status: Proposed (issue to be filed)

A user installs Winland by running one PowerShell line, `irm https://raw.githubusercontent.com/dante-sparras/winland/main/scripts/install.ps1 | iex`. The script asks two questions: whether to run Winland as administrator (Enter means yes, no gives the `--user` install), and whether to turn off Snap and the touchpad swipes (ADR 0014). It downloads `Winland-win-x64.zip` and `SHA256SUMS` from the newest published release, refuses a zip whose hash does not match, and runs `winland install`. Run again on a machine where Winland is installed, it asks nothing and updates.

`winland update` reads the newest published release's `version.json`, stops when Winland is current, and otherwise downloads and checks the zip, stops Winland, replaces the installed files, and starts the new version. The config is kept. Winland never checks for updates by itself. The zip on the release page and `winland install` stay for installing by hand.

Downloading a zip, finding the right folder and running a command from it was too many steps for a first try. A script fetched by PowerShell gets no download mark, so the unsigned build passes without the SmartScreen warning, and the checksum still holds it to what the release published. Building from source like a git-based installer would need git and the .NET SDK on every machine, and a `main` channel would ship untested commits, so there is one channel: published releases.
