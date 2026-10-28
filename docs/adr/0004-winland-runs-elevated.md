# Winland runs elevated by default

Winland runs as an administrator unless the user chooses to run it as the normal user. Windows blocks a non-elevated process from moving, resizing or focusing the windows of an elevated one, and its keyboard hook gets no keys while such a window has focus. A Task Manager or an admin terminal that ignores the layout and swallows the binds is worse than one UAC approval at install.

A user who does not want an elevated Winland can run it as the normal user and accept that elevated windows stay outside its control.

A manual start asks for UAC only when its token is the limited half of an administrator's split token. A standard user continues unelevated, without a prompt for another account's credentials. A second start checks the session lock before asking for approval.

The cost is that an elevated Winland needs elevation to start, and everything it launches inherits it unless that is undone. Both are settled where they arise, not here.
