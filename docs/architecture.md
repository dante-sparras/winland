# Architecture

Where things are, and the path an input takes. The rules behind the layout are in [ADR 0005](adr/0005-clean-architecture-in-four-layers.md); the words are in [CONTEXT.md](../CONTEXT.md).

## Layers

Each project references only the layers above it in this list. A test in each layer's test project fails when it references one outside it.

| Project | Holds |
| --- | --- |
| `src/Winland.Domain` | The model and the config language. No other project, no package, no Win32. |
| `src/Winland.Application` | `WindowManager`, the command protocol and the event stream. `Ports/` holds what an outer layer implements. |
| `src/Winland.Infrastructure` | Win32: hooks, window placement, monitors, the tray, the IPC pipe server. |
| `src/Winland.Install` | Install, update and release files. |
| `src/Winland.Daemon` | `winland.exe`: reads the config and wires the rest together in `Program.cs`. `ControlCommand` sends one command down the pipe. |

## How an input flows

1. A hook in `Infrastructure/Input` sees a key or the mouse. `WindowManager.Decide` matches binds on the hook thread without the lock, so the hook can swallow the key at once.
2. The decision, a window event from `Infrastructure/Window/WindowHost.cs`, or a pipe command from `Infrastructure/Dispatch/CommandServer.cs` goes to `WindowManager.Handle` or `WindowManager.Execute`. Each runs as one step under one lock.
3. The step returns an `Outcome`: placements, focus, windows to show and hide, launches, events. `WindowAdapter.Apply` in `Infrastructure/Window/WindowAdapter` carries it out.

Nothing in Domain or Application calls Win32. A test drives `WindowManager` with inputs and reads the `Outcome`.

## Where to change things

| To change | Look in |
| --- | --- |
| A dispatcher | `Application/WindowManager/DispatcherTable.cs` for the entry, `Domain/Bind/Dispatchers.cs` for the name. A test keeps the two equal. |
| A config option | The `*Options.cs` file of its section in `Domain/Config`, for example `LayoutOptions.cs` for `general:gaps_in`. `AppliedConfig.cs` holds every section, and a new section gets one line in `OptionTable.cs`, which the parser and `getoption` read. |
| A window rule | `Domain/WindowRule`, with its parser in `Domain/WindowRule/WindowRuleParser`. |
| A Layout | `Domain/Layout`: one folder per Layout, such as `DwindleLayout/`. |
| Which Client has focus, what each Workspace gives focus back to, and the focus history | `Domain/Focus/FocusState.cs`. When a window may take focus is in `Application/WindowManager/Focus.cs`. |
| Which Workspaces there are, their numbers, Guests, and which one a Monitor shows | `Domain/Workspace/WorkspaceSet.cs`, with `ShownWorkspaces.cs` for what each Monitor shows and `workspace previous`. `Application/WindowManager/Hotplug.cs` calls it when Monitors change. |
| Which windows may stay over a fullscreen or maximized Client | `Domain/Fullscreen/FullscreenCover.cs`. Pinning is in `Application/WindowManager/Pin.cs`. |
| Binds and keys | `Domain/Bind`. The hooks that feed them are in `Infrastructure/Input`. |
| The pointer, scrolling and the touchpad | `Domain/Pointer`. |
| A pipe request, such as a query or `keyword` | The verb in `Application/Dispatch/CommandProtocol/CommandProtocol.cs`, its reply in `Application/WindowManager/Queries.cs`, and the reply text in the other files of `CommandProtocol`. |
| An event in the Event stream | `Application/WindowManager/EventStream.cs`. |
| How windows move on screen | `Application/WindowManager/Animations.cs` plans motions, and `Infrastructure/Motion` plays them. |

`WindowManager` is one class in many partial files under `Application/WindowManager`. Each file opens with a one-line summary of what it holds.

## Tests

Each layer has its own test project under `tests/`, with folders that match the source. The helpers the Application tests share, such as `Display` for a Monitor and `BoundsOf` for where a window went, are in `tests/Winland.Application.Tests/Support` and need no import. `tests/Winland.Application.Tests/Conformance` rebuilds the reference test harness's desktop on `WindowManager` and sends it the reference cases' commands. `docs/behavior.md` is the spec ([ADR 0008](adr/0008-the-spec-is-a-pinned-hyprland-release.md)).
