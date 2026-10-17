# Conformance

The pinned release ships integration tests in `hyprtester/`. Each case starts the compositor, opens windows, sends commands over its socket, and checks exact results, such as where a window is and how big it is. Those cases are the most exact statement of Hyprland's behavior there is, so Winland rebuilds them as core tests in `tests/Winland.Core.Tests/Conformance/`. A rebuilt case is a conformance case.

Honored behavior is in [behavior.md](behavior.md) and Gaps are in [gaps.md](gaps.md). This page tracks how many of the reference cases Winland reproduces.

## Score

75 of 197 ported cases pass.

A case passes when every check in it passes, as in the reference harness. A case that differs in one check differs as a whole.

## How a case is ported

- The desktop is the reference harness's: one 1920x1080 monitor, HEADLESS-2, at 0,0, its config, workspace 1 focused and the pointer at 960,540. `Desk` in the conformance folder builds it. A monitor a case creates goes to the right of HEADLESS-2, as the reference's monitor rule puts it.
- Each test is named after its case, with the first letter of each part between underscores capitalized and the underscores dropped, so `groups_disable_when_only` is `GroupsDisableWhenOnly`. It sends the same commands in the same order. The reference config and dispatchers are Lua calls, and a test sends the line-based form: `hl.dsp.focus({ window = 'class:a' })` is `focuswindow class:a`, `hl.dsp.layout('togglesplit')` is `layoutmsg togglesplit`, and `hl.config({ dwindle = { force_split = 2 } })` is `keyword dwindle:force_split 2`.
- An option the case sets before its first window opens goes into the starting config. The reference sets it over the socket only because its compositor is already running. An option the case changes later goes through `keyword`.
- Opening kitty with a class is a window that appears with that class on the focused monitor, with a process of its own. Killing it is the window closing.
- `Desk` answers the core as the Windows adapter does: a window the core focuses reports focus back, and the pointer goes where the core warps it.
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
| dwindle | dwindleTestFsingGroupedWindows | differs | A hidden Group member keeps its last frame, where the pinned release gives it the shown member's fullscreen frame. A maximized floating Group member is not moved until the Group changes, and then covers the group bar. |
| dwindle | dwindleTestFsFocusUnderFSWindow | differs | misc:on_focus_under_fullscreen is a Gap, and maximize does not leave gaps_out and the border around the Client. |
| dwindle | dwindleNewWindowTakesOverFullscreen | differs | misc:on_focus_under_fullscreen is a Gap. |
| dwindle | dwindleExitWindowRetainsFullscreen | differs | misc:exit_window_retains_fullscreen is a Gap. |
| dwindle | dwindleFullscreenPinnedWindows | differs | binds:allow_pin_fullscreen is a Gap, and maximize does not leave the border around the Client. |
| dwindle | dwindleFullscreenNonInterference | passes | |
| dwindle | defaultHandledFsfocusInDirection | differs | binds:movefocus_cycles_fullscreen and misc:on_focus_under_fullscreen are Gaps. |
| dwindle | dwindleFullsreenCenterDispatchSavesPos | not ported | The result depends on the floating size kitty asks for, which a core test cannot match. |
| dwindle | dwindleFloatingOntopFullscreenWorkspaceFocusRetention | passes | |
| scroll | scrollFocusCycling | passes | |
| scroll | scrollFocusWrapping | passes | |
| scroll | scrollSwapcolWrapping | passes | |
| scroll | scrollWindowRule | passes | |
| scroll | scroll_LAYOUT_HANDLED_fullscreen | passes | |
| scroll | scroll_LAYOUT_HANDLED_maximized | differs | maximize does not leave gaps_out and the border around the Client. |
| scroll | scroll_LAYOUT_HANDLED_TestFsingGroupedWindows | differs | A hidden Group member keeps its last frame. |
| scroll | scroll_LAYOUT_HANDLED_fullscreenRetainsGeometryWhileScrolling | differs | fullscreenHandler reports default, and a fullscreen Client stays over the screen when focus moves to another column. fullscreenstate does not take a window. |
| scroll | scroll_LAYOUT_HANDLED_floatingWindowHiding | differs | A floating Client under a fullscreen one stays visible and accepts input, and a second fullscreen Client ends the first one's fullscreen. allowedOverFullscreen is always false. |
| scroll | scroll_LAYOUT_HANDLED_layerVisibilityOnFs | not ported | The case opens a layer shell surface, which has no Windows meaning. |
| scroll | scroll_LAYOUT_HANDLED_focusInDirectionFocusFollowFocusTrue | differs | binds:movefocus_cycles_fullscreen and misc:on_focus_under_fullscreen are Gaps. |
| scroll | scroll_LAYOUT_HANDLED_respectFocusFitMethodOnUnFs | differs | Leaving fullscreen does not fit the column into view by scrolling:focus_fit_method. |
| scroll | scroll_LAYOUT_HANDLED_FloatingOntopFullscreenWorkspaceFocusRetention | passes | |
| scroll | scroll_DEFAULT_HANDLED_fullscreenMaximiseDispatchers | differs | fullscreenstate does not read its set or toggle argument, so the same pair twice turns both modes off. |
| scroll | scroll_DEFAULT_HANDLED_TestFsingGroupedWindows | differs | A hidden Group member keeps its last frame, and maximize does not leave room for the group bar. |
| scroll | scroll_DEFAULT_HANDLED_testFsFocusUnderFSWindow | differs | misc:on_focus_under_fullscreen is a Gap, and maximize does not leave gaps_out and the border around the Client. |
| scroll | scroll_DEFAULT_HANDLED_newWindowTakesOverFullscreen | differs | misc:on_focus_under_fullscreen is a Gap. |
| scroll | scroll_DEFAULT_HANDLED_exitWindowRetainsFullscreen | differs | misc:exit_window_retains_fullscreen is a Gap. |
| scroll | scroll_DEFAULT_HANDLED_FullscreenPinnedWindows | differs | binds:allow_pin_fullscreen is a Gap, and maximize does not leave the border around the Client. |
| scroll | scroll_DEFAULT_HANDLED_FullscreenNonInterference | differs | scrolling:follow_min_visible also holds back focuswindow, and a Client out of view keeps its maximized frame after maximize ends. |
| scroll | scroll_DEFAULT_HANDLED_FloatingOntopFullscreenWorkspaceFocusRetention | passes | |
| scroll | scroll_DEFAULT_HANDLED_posAndSizeSyncWhileFS | not ported | The case opens a layer shell surface, which has no Windows meaning. |
| scroll | scrollNegativeScaleGesture | differs | The scrollMove gesture action is a Gap. |
| scroll | testScrollingViewBehaviourDispatchFocusWindowFollowFocusFalse | passes | |
| scroll | testScrollingViewBehaviourDispatchFocusWindowFollowFocustrue | differs | scrolling:follow_min_visible also holds back focuswindow, and a scroll that focuswindow makes places no window. |
| scroll | testScrollingViewBehaviourFocusFallback | passes | |
| scroll | testScrollingViewBehaviourFocusFallbackWithGroups | passes | |
| scroll | testScrollingViewBehaviourWorkspaceChange | passes | |
| scroll | testScrollingViewBehaviourSpecialWorkspaceChange | passes | |
| scroll | testScrollingViewBehaviourSpecialToSpecialWorkspaceChange | passes | |
| scroll | testScrollingViewBehaviourCloseWindowInGroup | passes | |
| scroll | testScrollingViewBehaviourMoveWindowIntoGroupFollowFocusFalse | passes | |
| scroll | testScrollingViewBehaviourMoveWindowInGroupFollowFocusTrue | differs | scrolling:follow_min_visible also holds back the focus moveintogroup gives. The pinned release applies it only to pointer focus. |
| scroll | testScrollingViewBehaviourNewLayer | not ported | The case opens a layer shell surface, which has no Windows meaning. |
| scroll | testScrollingViewBehaviourMoveFocusFollowFocusFalse | differs | movefocus scrolls the strip even with scrolling:follow_focus false. The scroll places no window, so the check reads the frame from before it. |
| scroll | testScrollingViewBehaviourMoveFocusFollowFocusTrue | differs | movefocus scrolls the strip but places no window. |
| scroll | testScrollingViewBehaviourMoveFocusInGroupFollowFocusFalse | passes | |
| scroll | testScrollingViewBehaviourMoveFocusInGroupFollowFocusTrue | differs | scrolling:follow_min_visible also holds back the focus moveintogroup and movefocus give inside a Group. The pinned release applies it only to pointer focus. |
| scroll | testScrollingViewBehaviourScheduledPropRefresh | passes | |
| scroll | testScrollInhibitor | passes | |
| scroll | layoutmsg_fit_into_view | passes | |
| scroll | layoutRuleExpand | passes | |
| scroll | scrollTapeOnClickOutOfWindow | not ported | The case soft-focuses a window with a test plugin call and clicks a pointer button, and Windows, not Winland, focuses a window on a click. |
| scroll | scrollTapeWithMouseClick | not ported | The case tells a button press from its release, and Windows, not Winland, focuses a window on a click. |
| scroll | properFocusBehvaior | differs | movefocus wraps within the strip instead of crossing to the next Monitor. |
| window | swapWindow | differs | swapwindow answers can't swap fullscreen window in lower case, and does not swap with a Client on another Workspace. |
| window | windowGroupRules | differs | getprop does not honor border_size, and the border_size window rule is not honored. |
| window | windowFocusOnFullscreenConflict | differs | misc:on_focus_under_fullscreen is a Gap. |
| window | focusFloatingOrTilingUnderFullscreen | differs | misc:on_focus_under_fullscreen is a Gap. |
| window | windowMaximizeSize | differs | Maximize does not leave gaps_out and the border around the Client. |
| window | floatingFocusOnFullscreen | not ported | Its only check is a call into the test plugin, which a core test cannot make. |
| window | groupFallbackFocus | differs | Closing the shown Group member shows its neighbour. The pinned release shows the member focused last. |
| window | bringActiveToTopMouseMovement | differs | movecursor is not a dispatcher, and clients does not list Clients in stacking order. |
| window | initialFloatSize | not ported | The result depends on the floating size kitty asks for, which a core test cannot match. |
| window | windowRuleFocusOnActivate | differs | The focus_on_activate window rule is not honored. |
| window | pinnedWorkspacesValid | passes | |
| window | windowruleWorkspaceEmpty | passes | |
| window | contentWindowRules | differs | The content window rule is not honored, and getprop does not honor border_size or opacity. |
| window | issue14038 | passes | |
| window | issue14134 | passes | |
| window | specialFloatRecenters | differs | A floating Client moved off its Monitor stays there when its special workspace is shown. The pinned release centers it. |
| window | exactWindowSelectors | differs | A stableid: window selector matches no Client. |
| window | windows | differs | A w[tv1] workspace rule is matched before a new window is counted, and getprop does not read min_size or max_size. |
| window | cycle_nextTiled | differs | cyclenext is not a dispatcher. |
| window | cycle_nextFloating | differs | cyclenext is not a dispatcher. |
| window | discussion14233 | differs | The border_size window rule is not honored, and a w[tv1] workspace rule is matched before a new window is counted. |
| window | execRulesWorkspaceOverride | passes | |
| window | execRulesTagMutation | passes | |
| window | execRulesDescendantProcess | differs | getprop and setprop do not honor no_focus or border_size. |
| window | execRulesPendingIsolation | differs | getprop and setprop do not honor no_focus. |
| window | pinnedRetainsPositionOnWorkspaceChange | passes | |
| window | monitorrule | passes | |
| window | mouseResize | differs | movecursor is not a dispatcher, and a mouse bind pressed on a Client's border does not take the Client. The keep_aspect_ratio window rule is not honored. |
| window | getFullscreenHandler | differs | fullscreenHandler is always default. The pinned release reports scrolling for a tiled Client in the scrolling Layout. |
| window | sendFsWindowToAnotherWorkspace | differs | Maximize does not leave the border around the Client, and fullscreenHandler is always default. |
| window | floatingForceOnscreen | passes | |
| window | specialWorkspacePinnedWindowUnmapFallback | differs | Closing a focused pinned Client focuses a Client on the normal Workspace under an open special workspace. The pinned release focuses the special workspace's Client. |
| window | windowRuleBorderColors | differs | getprop does not honor active_border_color or inactive_border_color, and the border_color window rule is not honored. |
| window | windowRuleBorderColorsFocus | differs | getprop does not honor active_border_color or inactive_border_color, and the border_color window rule is not honored. |
| window | floatingMoveExpression | passes | |
| window | newFullscreenWindowReceivesCursorFocus | not ported | The case returns before its first check in the pinned release. |
| window | windowGoesFullscreenAndReceivesCursorFocus | not ported | The case returns before its first check in the pinned release. |
| window | windowThatGoesFullscreenBecomesActive | not ported | The case returns before its first check in the pinned release. |
| groups | groupDimensions | passes | |
| groups | groupNext | passes | |
| groups | groupMoveWindow | passes | |
| groups | autoGroupInsertAfterCurrent | passes | |
| groups | moveWindowOrGroup | passes | |
| groups | groupLock | passes | |
| groups | groupbarMiddleClick | not ported | The case middle-clicks the tab bar, which the Windows adapter handles and a core test cannot send. |
| groups | groupsNoCrash | passes | |
| groups | groupsLuaApi | not ported | The case drives the Lua group object API, which has no dispatcher form. |
| groups | groupsLuaApiInvalidOps | not ported | The case drives the Lua group object API, which has no dispatcher form. |
| groups | groupsLuaApiCrossWorkspaceMonitor | not ported | The case drives the Lua group object API, which has no dispatcher form. |
| groups | groupsLuaApiFullscreen | not ported | The case drives the Lua group object API, which has no dispatcher form. |
| groups | groups_disable_when_only | passes | |
| groups | groupbarDisableWhenOnlyDimensions | differs | A hidden Group member keeps its last frame. The pinned release gives it the frame of the shown member. |
| groups | autoGroupFloatedIntoTiled | passes | |
| groups | autoGroupFloatedintoFloated | passes | |
| groups | autoGroupTiledIntoFloated | passes | |
| groups | groupedExitWindowRetainsFullscreen | differs | misc:exit_window_retains_fullscreen is a Gap. |
| groups | windowOpenInGroupReceivesCursorFocus | not ported | The case checks that kitty receives a real mouse click, which a core test cannot send. |
| moveintoorcreategroup | moveIntoOrCreateGroup | passes | |
| moveintoorcreategroup | crossMonitorGroupJoin | differs | moveintoorcreategroup does not find a Client on another Monitor, so the Client stays in its own Group. The pinned release looks for the neighbour across Monitors. |
| workspaces | workspacesCombined | differs | binds:movefocus_cycles_fullscreen and general:no_focus_fallback are Gaps. movewindow r does not carry the rightmost tile onto the next Monitor, and getprop does not answer border_size or rounding. |
| workspaces | workspacesFollowProperNoGaps | differs | A w[tv1] workspace rule is not applied when the first Client opens on a Workspace. The bordersize window rule and the onworkspace match are not honored. |
| workspaces | workspacesSmartGapsDirectionalMoveAcrossMonitors | differs | A w[tv1] workspace rule is not applied when the first Client opens on a Workspace, and the bordersize window rule is not honored. movewindow r does not carry the rightmost tile onto the next Monitor. |
| workspaces | workspaceRenameChangeID | differs | There is no dispatcher that changes a Workspace's id, and renameworkspace is not a dispatcher. |
| workspaces | workspaceRenameEmitsGlobalEvent | differs | renameworkspace is not a dispatcher. A Workspace keeps the name it was created with. |
| workspaces | windowAtExplicitWorkspace | not ported | The case calls a hit test on a hidden Workspace through the reference's test plugin, which no command reaches. |
| workspaces | workspaceChangeIDUpdatesRules | differs | There is no dispatcher that changes a Workspace's id. |
| workspaces | workspaceRenameDoesNotChangeAddress | differs | renameworkspace is not a dispatcher. A Workspace keeps the name it was created with. |
| workspaces | workspaceRulesMatchBareAddressableNames | differs | renameworkspace is not a dispatcher. A Workspace keeps the name it was created with. |
| workspaces | luaGetWorkspace | not ported | The case reads workspace objects from the Lua API, which a line-based command cannot reach. |
| workspaces | luaSetWorkspace | not ported | The case sets workspaces through monitor objects of the Lua API, which a line-based command cannot reach. |
| workspaces | luaSetSpecialWorkspace | not ported | The case sets special workspaces through monitor objects of the Lua API, which a line-based command cannot reach. |
| workspaces | workspacesDistinctTiledAndFloatGaps | differs | A window rule cannot match on workspace, and floating gaps on a workspace rule are the float_gaps Gap. |
| workspaces | workspaceSwitchUnfocusesWindowFromOldWorkspace | passes | |
| workspaces | luaFocusCreatesBareNamedWorkspace | passes | |
| persistent | persistentWorkspaces | differs | A persistent rule keeps a special workspace, and creates a Workspace bound to a Monitor that is not there yet. A new Monitor then shows such a Workspace, where the pinned release opens workspace 2. |
| persistent | workspaceLifecycleReleasesEmptyWorkspaces | differs | An empty Workspace on a Monitor that goes away is kept on the Monitor that takes it. The pinned release removes it. |
| monitor_rules | monitorMirrorAppliedWhenTargetAppears | not ported | The case checks which render passes a mirrored monitor draws, through the reference's test plugin. |
| monitor_rules | monitorBackgroundWorkspaceAndXpMode | not ported | The case checks which render passes a monitor's background draws, through the reference's test plugin. |
| master | focusMasterPrevious | passes | |
| master | masterTestFsFocusUnderFSWindow | differs | misc:on_focus_under_fullscreen is a Gap, and maximize does not leave gaps_out and the border around the Client. |
| master | masterFullscreenMaximiseDispatchers | differs | fullscreenstate does not read its set or toggle argument, so the same pair twice turns both modes off. |
| master | masterNewWindowTakesOverFullscreen | differs | misc:on_focus_under_fullscreen is a Gap. |
| master | masterExitWindowRetainsFullscreen | differs | misc:exit_window_retains_fullscreen is a Gap. |
| master | masterFullscreenPinnedWindows | differs | binds:allow_pin_fullscreen is a Gap, and maximize does not leave the border around the Client. |
| master | masterFullscreenNonInterference | passes | |
| master | rollFocus | passes | |
| master | focusMasterClose | passes | |
| master | centerMasterColumnResize | passes | |
| master | masterCenterDropAtCursor | passes | |
| master | masterDropAtCursor | passes | |
| monocle | monocleWorkspaceRule | differs | A w[] workspace rule is not matched again when a Client opens, and closing the shown Client in monocle shows the first Client, not the one focused last. |
| monocle | monocleFullscreenMaximiseDispatchers | differs | fullscreenstate does not read its set or toggle argument, so the same pair twice turns both modes off. |
| monocle | monocleTestFsFocusUnderFSWindow | differs | misc:on_focus_under_fullscreen is a Gap, and maximize does not leave gaps_out and the border around the Client. |
| monocle | monocleNewWindowTakesOverFullscreen | differs | misc:on_focus_under_fullscreen is a Gap. |
| monocle | monocleExitWindowRetainsFullscreen | differs | misc:exit_window_retains_fullscreen is a Gap. |
| monocle | monocleFullscreenPinnedWindows | differs | binds:allow_pin_fullscreen is a Gap, and maximize does not leave the border around the Client. |
| monocle | monocleFullscreenNonInterference | passes | |
| layout | single_window_aspect_ratio | differs | layout:single_window_aspect_ratio is not read, and maximize does not leave gaps_out and the border around the Client. |
| layout | crashOnGeomUpdate | differs | The monitor keyword refuses a mode and a scale, which are Gaps. |
| layout | layoutRefreshDuringMonitorTeardown | differs | The monitor keyword refuses a mode and a scale, which are Gaps. |
| layout | posPreserve | differs | movewindow r puts a floating Client's frame against the monitor edge, where the pinned release keeps its border inside, and binds:allow_pin_fullscreen is a Gap. |
| layout | focusMRUAfterClose | passes | |
| layout | focusPreservedLayoutChange | passes | |
| layout_custom | layoutCustomGrid | not ported | A custom layout is registered with hl.layout.register, which waits on the Lua config (ADR 0013). |
| layout_custom | layoutCustomColumns | not ported | A custom layout is registered with hl.layout.register, which waits on the Lua config (ADR 0013). |
| drag | dragTiledCentered | differs | movecursor is not a dispatcher. |
| drag | dragTiledProportional | differs | movecursor is not a dispatcher, and binds:drag_center_window is not read, so a lifted tile is always centered on the pointer. |
| drag | dragFullscreenCentered | differs | movecursor is not a dispatcher, and a movewindow drag leaves a fullscreen Client fullscreen where it is. |
| drag | dragFullscreenProportional | differs | movecursor is not a dispatcher, binds:drag_center_window is not read, and a movewindow drag leaves a fullscreen Client fullscreen where it is. |
| drag | dragLifecycleSignals | not ported | The case checks the signals of the drag controller inside the compositor through the test plugin, which the core has no counterpart for. |
| drag | dragThresholdSurvivesScrollEvents | differs | movecursor is not a dispatcher. |
| snap | snap | passes | |
| focus | errorBarReservationFollowsFocusedMonitor | differs | seterror is not a request. |
| focus | crossMonitorFullscreenFocus | differs | movefocus onto another Monitor focuses the tile under its fullscreen Client. The pinned release focuses the fullscreen Client. |
| focus | silentMoveLastTiledWindowUnfocusesWindow | passes | |
| focus | crossMonitorEmptyWorkspaceUnfocusesWindow | passes | |
| follow_mouse_shrink | followMouseShrink | differs | movecursor is not a dispatcher. |
| cursor | cursorPerOutputHyprcursor | not ported | The case checks the cursor image each output renders from a cursor theme, which a core test cannot see. |
| hyprctl | hyprctlDevicesActiveLayoutIndex | differs | input:kb_layout is a Gap, and there is no switchxkblayout or devices request. |
| hyprctl | hyprctlGetprop | differs | getprop and setprop honor only float, and there is no setprop dispatcher. |
| hyprctl | hyprctlSubmap | differs | There is no submap request. |
| hyprctl | hyprctlJsonErrors | differs | There is no descriptions request. |
| hyprctl | hyprctlDescriptionsCurrentValues | differs | There is no descriptions request. |
| hyprctl | hyprctlBindsJson | passes | |
| hyprctl | hyprctlWorkspaceJson | differs | There is no renameworkspace dispatcher. The monitors and workspaces replies give a special workspace an id and give no Workspace a type. |
| hyprctl | hyprctlREPL | not ported | It runs Lua code through repl and eval. The Lua config waits on ADR 0013. |
| hyprctl | hyprctlBatch | not ported | Its batch runs Lua code through repl. The Lua config waits on ADR 0013. |
| misc | misc | passes | |
| misc | processesThatDieEarlyAreReaped | not ported | It checks that the OS reaps a process the config starts, which a core test cannot see. |
| state | state | differs | Turning every monitor off leaves no monitor. The pinned release adds a FALLBACK monitor that keeps the Workspaces. |
| refactor_state | workspacePlacementActiveMove | passes | |
| refactor_state | workspacePlacementWindowState | passes | |
| refactor_state | globalWindowMoveState | differs | movetoworkspace keeps a floating Client's position when the Workspace is on another Monitor. The pinned release moves it by the offset between the Monitors. |
| refactor_state | pointerNoWarpsFocusesMonitorOnly | differs | There is no movecursor dispatcher. |
| refactor_state | cursorWarpFocusOnMonitorChange | differs | There is no movecursor dispatcher. |
| solitary | solitaryClients | not ported | Solitary, tearing and direct scanout are render paths, which a core test cannot see. |
| colors | monitorsColorManagement | differs | bitdepth and cm are Gaps. |
| animations | animationsTrivial | differs | There is no animations request. |
| layer | plugin_layerrules | not ported | It opens layer shell surfaces and checks a layer rule through the test plugin. |
| layer | layerPointerFocusPreservedOnKeyboardRefocus | not ported | It opens a layer shell surface and reads the seat's pointer focus through the test plugin. |
| layer | exclusiveLayerTakesPointerFocusFromRuleConfinedWindow | not ported | It opens an exclusive layer shell surface and reads the seat's focus through the test plugin. |
| layer | ruleConfinedWindowFocusRestoredAfterEmptyWorkspace | not ported | It reads the seat's keyboard and pointer focus and the confined cursor through Lua and the test plugin. |
| layer | ruleConfinedWindowFocusRestoredAfterSpecialWorkspace | not ported | It reads the seat's keyboard and pointer focus and the confined cursor through Lua and the test plugin. |
| layer | ruleConfinedWindowFocusRestoredAfterCrossMonitorExclusiveLayer | not ported | It opens an exclusive layer shell surface and reads the seat's focus through Lua and the test plugin. |
| layer | ruleConfinementAllowsExplicitAndNewWindowFocusWithoutWarps | not ported | It reads the seat's keyboard focus and the confined cursor through Lua and the test plugin. |
| layer | ruleConfinementRuntimeToggle | not ported | It changes a named window rule through Lua and reads the seat's focus through the test plugin. |
| layer | layerVisibilityOnFs | not ported | It opens a layer shell surface and reads its alpha from the layers reply. |
| layer | windowRefocusRestoresKeyboardFocusAfterSurfaceFocusCleared | not ported | It clears and checks the seat's keyboard focus through the test plugin. |
| lua_config | luaRequire | not ported | It reads a Lua global set through require. The Lua config waits on ADR 0013. |
| lua_config | luaSpecialWorkspaceDeactivationEventNil | not ported | It subscribes a Lua function to an event. The Lua config waits on ADR 0013. |
| lua_config | luaDefaultSpecialWorkspaceName | not ported | Its second half calls a Lua method on a monitor. The Lua config waits on ADR 0013. |
| lua_config | luaEventConfigUnload | not ported | It subscribes a Lua function to an event. The Lua config waits on ADR 0013. |
| lua_config | luaReloadConfig | not ported | It reads a Lua global after a reload. The Lua config waits on ADR 0013. |
| lua_config | luaDevicesEnumerationAndHyprctlParity | not ported | It reads the devices through Lua. The Lua config waits on ADR 0013. |
| lua_config | luaDevicesRetainedKeyboardTracksLayoutChanges | not ported | It reads a keyboard through Lua. The Lua config waits on ADR 0013. |
| xwayland_selection | xwaylandSelectionSourceGuard | not ported | It checks X11 selections through the test plugin. |
| clients/child-window | childWindow | differs | A window owned by a pinned Client is not pinned with it. |
| clients/fullscreen-with-monitor | fullscreenWithExplicitMonitor | not ported | An application that asks to be fullscreen is not observed, so the core has no input for the request. |
| clients/keyboard-modifiers | keyboardModifiersMergedOnFocus | not ported | Windows keeps one keyboard state and gives the focused window its lock keys itself. |
| clients/pointer-constraints | nativePointerConstraintsPersistent | not ported | Pointer constraints are a Wayland protocol with no Windows meaning. |
| clients/pointer-constraints | nativePointerConstraintsOneshot | not ported | Pointer constraints are a Wayland protocol with no Windows meaning. |
| clients/pointer-constraints | nativePointerLockOverridesConfineRule | not ported | Pointer constraints are a Wayland protocol with no Windows meaning. |
| clients/pointer-scroll | pointerScroll | not ported | The reference returns before its first check, so the case checks nothing. |
| clients/pointer-warp | pointerWarp | not ported | The reference returns before its first check, so the case checks nothing. |
| clients/popup-render | popupOpacityInheritsParentFade | not ported | Winland does not draw windows or their popups, so a popup has no fade to inherit. |
| clients/popup-render | popupRespectsPassRedirection | not ported | Winland does not draw windows or their popups, so a popup has no fade to inherit. |
| clients/shortcut-inhibitor | shortcutInhibitor | not ported | Applications cannot inhibit binds, which is the dont_inhibit Gap. |
| clients/surface-scale-transform | surfaceScaleTransform | not ported | Surface scale and transform are Wayland protocol state. Windows scales a window by its DPI awareness. |
| clients/toplevel-capture | windowShareCaptureProducesFrame | not ported | Window capture is done by the system, not by Winland. |
| clients/wlr-foreign-toplevel | wlrForeignToplevelMinimizeEvents | passes | |
| clients/xdg-activation | xdgActivationSerial | differs | With misc:focus_on_activate a window that takes the foreground keeps it, without the fresh input serial the pinned release requires. There is no movecursor dispatcher. |
| clients/xdg-initial-maximize | xdgMaximizeBeforeInitialCommitIsIgnored | differs | A window shown maximized is maximized. The pinned release ignores a maximize request made before the window maps. |
| clients/xdg-initial-maximize | xdgMaximizeBeforeMapIsIgnored | differs | A window shown maximized is maximized. The pinned release ignores a maximize request made before the window maps. |
| clients/xdg-interactive | xdgInteractive | differs | There is no setprop or movecursor dispatcher. A floating Client the system moves or resizes keeps its old frame in activewindow. |
| misc/plugin | plugin | passes | |
| misc/plugin | pluginVkb | not ported | Windows merges every keyboard into one key state and does not tell Winland which device a key came from. |
| exec | processSpawning | passes | |
| gestures | pinchDeltaScalePropagation | not ported | It checks a gesture the test plugin registers through the C++ plugin interface, which has no core form. |
| gestures | defaultSpecialWorkspaceGesture | differs | The special gesture action is not honored. |
| gestures | live_gesture_callbacks | not ported | Gesture actions with start, update and end callbacks are Lua-only. |
| gestures | gestures | differs | Only the workspace gesture action is honored, and movecursor is not a dispatcher. |
| keybinds | keybinds | differs | A bind on several keys runs on each of them, a long press waits 500 ms instead of input:repeat_delay, and per-device binds are not honored. |
| keybinds | orderedSubChordDeferral | differs | A bind on several keys runs on each of them. The pinned release runs it once they are all pressed in order, and holds back a shorter bind that starts the same chord. |
| keybinds | modifierReleaseBindShadowing | differs | A release bind on a modifier key still runs after another bind used that modifier. The pinned release skips it. |
| keybinds | luaDispatcherStrings | not ported | It reads the handler of a bind as a Lua value, which is Lua-only. |
| keyboard_input | pluginKeyboardEventHandler | not ported | It checks a keyboard event handler registered through the C++ plugin interface, which has no core form. |
| keyboard_input | orderedChordConsumption | differs | A bind on several keys runs on each of them. The pinned release runs it once they are all pressed in order. |
| keyboard_input | orderedChordRejectsReorderedPrefix | differs | A bind on several keys runs on each of them. The pinned release runs it once they are all pressed in order. |
| keyboard_input | orderedReleaseChord | differs | A bind on several keys runs on each of them. The pinned release runs it once they are all pressed in order. |
| keyboard_input | nonConsumingChordPreservesOrder | differs | A bind on several keys runs on each of them. The pinned release runs it once they are all pressed in order. |
| swallow | swallow | differs | A Workspace's windows count includes a swallowed Client, toggleswallow on the swallowed Client does nothing, and a Client swallowed in turn loses the Client it swallowed. A window selector does not find a swallowed Client. |
| tags | tags | differs | The tag and nodim window rule effects are refused. getprop does not read no_dim or no_shadow. |
| tags | tags_fullscreen_cleanup | differs | tagwindow is not a dispatcher, and window tags are not tracked. |
| workspace_swipe | workspaceSwipeReversalCleanup | not ported | Most of its checks read render state through the test plugin: alpha, render offset, the render hold and the swipe session. The checks the replies can answer would make a partial case pass. |
| workspace_swipe | workspaceSwipeMissingNeighborCleanup | not ported | Most of its checks read render state through the test plugin: alpha, render offset, the render hold and the swipe session. The checks the replies can answer would make a partial case pass. |
| workspace_swipe | workspaceSwipeCancelCleanup | not ported | Most of its checks read render state through the test plugin: alpha, render offset, the render hold and the swipe session. The checks the replies can answer would make a partial case pass. |
| workspace_swipe | workspaceSwipeInvalidUpdateCleanup | passes | |
| workspace_swipe | workspaceSwipeInvalidEndCleanup | passes | |
| workspace_swipe | workspaceSwipePreexistingForce | passes | |
| workspace_swipe | workspaceSwipeCommitRollbackThresholds | not ported | Most of its checks read render state through the test plugin: alpha, render offset, the render hold and the swipe session. The checks the replies can answer would make a partial case pass. |
| workspace_swipe | workspaceSwipeSpeedThresholds | not ported | Most of its checks read render state through the test plugin: alpha, render offset, the render hold and the swipe session. The checks the replies can answer would make a partial case pass. |
| workspace_swipe | workspaceSwipeOriginOutputFocus | not ported | Most of its checks read render state through the test plugin: alpha, render offset, the render hold and the swipe session. The checks the replies can answer would make a partial case pass. |
| workspace_swipe | workspaceSwipeOriginInvalidated | not ported | Most of its checks read render state through the test plugin: alpha, render offset, the render hold and the swipe session. The checks the replies can answer would make a partial case pass. |
| workspace_swipe | workspaceSwipeOriginOutputSwapped | not ported | Most of its checks read render state through the test plugin: alpha, render offset, the render hold and the swipe session. The checks the replies can answer would make a partial case pass. |
| workspace_swipe | workspaceSwipeReentrantEndOwnership | not ported | It starts a second swipe from inside the first one's Workspace change, through the test plugin, which core input cannot do. |
| workspace_swipe | workspaceSwipeOriginOutputRemoved | not ported | Most of its checks read render state through the test plugin: alpha, render offset, the render hold and the swipe session. The checks the replies can answer would make a partial case pass. |
| workspace_swipe | workspaceSwipeForeverSession | not ported | Most of its checks read render state through the test plugin: alpha, render offset, the render hold and the swipe session. The checks the replies can answer would make a partial case pass. |
| workspace_swipe | workspaceSwipeTrackpadOwnership | not ported | It drives the compositor's swipe objects directly through the test plugin, which core input cannot do. |
| workspace_swipe | workspaceSwipeTouchDeviceIdentity | not ported | It swipes with touchscreen contacts, which have no core input. Touchscreen swipes are the gestures:workspace_swipe_touch Gap. |
| workspace_swipe | workspaceSwipeTouchConsumedAfterCancel | not ported | It swipes with touchscreen contacts, which have no core input. Touchscreen swipes are the gestures:workspace_swipe_touch Gap. |
| special_workspace_swipe | specialWorkspaceSwipeThresholds | differs | The special gesture action is not honored. |
| special_workspace_swipe | specialWorkspaceSwipeRestartAndDestructor | not ported | It destroys the gesture object mid-swipe through the test plugin, which core input cannot do. |
| special_workspace_swipe | specialWorkspaceSwipeReplacementInvalidation | differs | The special gesture action is not honored. |
| special_workspace_swipe | specialWorkspaceSwipeMoveInvalidation | differs | The special gesture action is not honored. |
| special_workspace_swipe | specialWorkspaceSwipeMoveExternalState | not ported | It sets the special workspace's alpha, offset and animation through the test plugin, which have no core form. |
| special_workspace_swipe | specialWorkspaceSwipeOutputRemoval | differs | The special gesture action is not honored. |
| special_workspace_swipe | specialWorkspaceSwipeReentrantEnd | not ported | It starts a second swipe from inside the first one's end, through the test plugin, which core input cannot do. |
| special_workspace_swipe | specialWorkspaceSwipeEmptyWorkspaceLifetime | differs | The special gesture action is not honored. |
