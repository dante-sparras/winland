# Winland manages windows and leaves the rest of the desktop to Windows

Status: Proposed (issue to be filed)

Winland places, sizes, focuses, hides and borders windows. Everything else on the desktop stays with Windows: the taskbar stays visible unless the config hides it; display modes, scale, rotation, HDR and refresh rate, mouse, keyboard, touchpad, pen and touch settings are set in Windows Settings; notifications are Windows notifications; blur, backdrops and shadows are DWM's; zoom is Magnifier. Winland draws only its borders and the tab bar of a Group, and its one touch input is the touchpad swipe between Workspaces.

A Windows user already sets these things in Settings and expects them to stay as set. A window manager that writes them is a second place to look when something changes, and some of them, such as a display mode or a scale, outlive Winland. Drawing effects Windows has no hook for meant capturing other programs' windows and covering them with overlays, about nine thousand lines of Direct3D and capture code with a long list of cases it could not handle. Winland began as a copy of a Wayland compositor, which owns all of this on Linux; on Windows it does not, and Winland stops pretending that it does.
