# Conformance

The pinned release ships integration tests in `hyprtester/`. Each case starts the compositor, opens windows, sends commands over its socket, and checks exact results, such as where a window is and how big it is. Those cases are the most exact statement of Hyprland's behavior there is, so Winland rebuilds them as core tests in `tests/Winland.Core.Tests/Conformance/`. A rebuilt case is a conformance case.

Honored behavior is in [behavior.md](behavior.md) and Gaps are in [gaps.md](gaps.md). This page tracks how many of the reference cases Winland reproduces.

## Score

8 of 16 ported cases pass.

A case passes when every check in it passes, as in the reference harness. A case that differs in one check differs as a whole.

## How a case is ported

- The desktop is the reference harness's: two 1920x1080 monitors side by side, its config, workspace 1 focused and the pointer at 960,540. `Desk` in the conformance folder builds it.
- Each test is named after its case, with the first letter capitalized, and sends the same commands in the same order. The reference config and dispatchers are Lua calls, and a test sends the line-based form: `hl.dsp.focus({ window = 'class:a' })` is `focuswindow class:a`, `hl.dsp.layout('togglesplit')` is `layoutmsg togglesplit`, and `hl.config({ dwindle = { force_split = 2 } })` is `keyword dwindle:force_split 2`.
- An option the case sets before its first window opens goes into the starting config. The reference sets it over the socket only because its compositor is already running. An option the case changes later goes through `keyword`.
- Opening kitty with a class is a window that appears with that class. Killing it is the window closing.
- `at` and `size` are the frame Winland places the window at, inside its border. That is what the reference replies report. Winland's own `clients` and `activewindow` replies report the Layout slot instead.
- A case that cannot pass is skipped with the reason it differs. The reason in the code and in the table below are the same, and a test checks that they are.

## Cases

`passes` runs and passes. `differs` is skipped with the reason. `not ported` has no test, for the reason given.

| file | case | status | note |
| --- | --- | --- | --- |
| dwindle | dwindleFloatClamp | passes | |
| dwindle | dwindleIssue13349 | passes | |
| dwindle | dwindleSplit | differs | splitratio on a Workspace with one Client answers ok. The pinned release refuses it. |
| dwindle | dwindleRotateSplit | passes | |
| dwindle | dwindleForceSplitOnMoveToWorkspace | passes | |
| dwindle | dwindleMoveAcrossToggledSplit | passes | |
| dwindle | dwindleMoveSmallWindowAcrossSplit | passes | |
| dwindle | dwindleFullscreenMaximiseDispatchers | differs | fullscreenstate does not read its set or toggle argument, so the same pair twice turns both modes off. |
| dwindle | dwindleTestFsingGroupedWindows | differs | A hidden Group member keeps its last frame. The pinned release gives it the fullscreen frame of the shown member. |
| dwindle | dwindleTestFsFocusUnderFSWindow | differs | misc:on_focus_under_fullscreen is a Gap, and maximize does not leave gaps_out and the border around the Client. |
| dwindle | dwindleNewWindowTakesOverFullscreen | differs | misc:on_focus_under_fullscreen is a Gap. |
| dwindle | dwindleExitWindowRetainsFullscreen | differs | misc:exit_window_retains_fullscreen is a Gap. |
| dwindle | dwindleFullscreenPinnedWindows | differs | binds:allow_pin_fullscreen is a Gap. |
| dwindle | dwindleFullscreenNonInterference | passes | |
| dwindle | defaultHandledFsfocusInDirection | differs | binds:movefocus_cycles_fullscreen and misc:on_focus_under_fullscreen are Gaps. |
| dwindle | dwindleFullsreenCenterDispatchSavesPos | not ported | The result depends on the floating size kitty asks for, which a core test cannot match. |
| dwindle | dwindleFloatingOntopFullscreenWorkspaceFocusRetention | passes | |
