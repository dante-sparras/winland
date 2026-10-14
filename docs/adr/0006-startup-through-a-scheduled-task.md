# Winland starts at logon through a scheduled task

`winland install` registers a scheduled task that runs Winland at logon with the highest privileges, and `winland uninstall` removes it. The install needs one UAC approval. After that Winland starts without a prompt.

The Run key and Startup folder can't start an elevated program silently. A Windows service runs in session 0, where it can't touch the user's windows.
