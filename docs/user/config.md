# Settings

Winland keeps `%USERPROFILE%\.config\winland`.

| File | Role |
| --- | --- |
| `binds.json` | Your key map. With no file, Winland starts from its own built-in binds. A file with no `include` is the whole map |
| `settings.json` | Spacing, square tiles, and whether windows that run as administrator are tiled |

A file may start with `"include": ["path", ...]`. The paths are relative to that file. The included files are read in order and merged: objects merge, a list or a single value replaces, and `null` removes a key. Your file applies last, so one key or one chord is enough. Binds merge one chord at a time, and `"command": null` removes that chord. A missing include is logged and skipped.

Winoma ships the binds it wants you to start from as `%LOCALAPPDATA%\Winoma\defaults\winland`, and a `binds.json` Winoma writes includes that copy.
