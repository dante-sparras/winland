# Winland installs itself and starts at logon

Status: Proposed (issue to be filed)

`winland install` asks for one UAC approval, copies Winland into `C:\Program Files\Winland`, and registers a scheduled task that runs it at logon with the highest privileges. After that Winland starts without a prompt. `winland install --user` copies it into `%LOCALAPPDATA%\Programs\Winland` and starts it at logon from the user's Run key, unelevated and without a prompt. `winland uninstall` removes the task or the Run key entry and the installed files and keeps the config; `--purge` also removes the config, the logs and the state.

The Run key and Startup folder can't start an elevated program silently, and a Windows service runs in session 0, where it can't touch the user's windows. The elevated task must start a file only administrators can change, or any program the user runs could replace what it starts, so the install puts Winland under Program Files itself instead of asking the user to copy it there.
