# Conformance

The pinned release ships integration tests in `hyprtester/`. Each case starts the compositor, opens windows, sends commands over its socket, and checks exact results, such as where a window is and how big it is. Those cases are the most exact statement of Hyprland's behavior there is, so Winland rebuilds them as core tests in `tests/Winland.Core.Tests/Conformance/`. A rebuilt case is a conformance case.

Honored behavior is in [behavior.md](behavior.md) and Gaps are in [gaps.md](gaps.md). This page tracks how many of the reference cases Winland reproduces.

## Score

186 of 197 ported cases pass.

A case passes when every check in it passes, as in the reference harness. A case that differs in one check differs as a whole.

## How a case is ported

- The desktop is the reference harness's: one 1920x1080 monitor, HEADLESS-2, at 0,0, its config, workspace 1 focused and the pointer at 960,540. `Desk` in the conformance folder builds it. A monitor a case creates goes to the right of HEADLESS-2, as the reference's monitor rule puts it.
- Each test is named after its case, with the first letter of each part between underscores capitalized and the underscores dropped, so `groups_disable_when_only` is `GroupsDisableWhenOnly`. It sends the same commands in the same order. The reference config and dispatchers are Lua calls, and a test sends the line-based form: `hl.dsp.focus({ window = 'class:a' })` is `focuswindow class:a`, `hl.dsp.layout('togglesplit')` is `layoutmsg togglesplit`, and `hl.config({ dwindle = { force_split = 2 } })` is `keyword dwindle:force_split 2`. `layout_aware = false` in a fullscreen dispatcher is a last word `default`.
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
| dwindle | dwindleSplit | passes | |
| dwindle | dwindleRotateSplit | passes | |
| dwindle | dwindleForceSplitOnMoveToWorkspace | passes | |
| dwindle | dwindleMoveAcrossToggledSplit | passes | |
| dwindle | dwindleMoveSmallWindowAcrossSplit | passes | |
| dwindle | dwindleFullscreenMaximiseDispatchers | passes | |
| dwindle | dwindleTestFsingGroupedWindows | passes | |
| dwindle | dwindleTestFsFocusUnderFSWindow | passes | |
| dwindle | dwindleNewWindowTakesOverFullscreen | passes | |
| dwindle | dwindleExitWindowRetainsFullscreen | passes | |
| dwindle | dwindleFullscreenPinnedWindows | passes | |
| dwindle | dwindleFullscreenNonInterference | passes | |
| dwindle | defaultHandledFsfocusInDirection | passes | |
| dwindle | dwindleFullsreenCenterDispatchSavesPos | not ported | The result depends on the floating size kitty asks for, which a core test cannot match. |
| dwindle | dwindleFloatingOntopFullscreenWorkspaceFocusRetention | passes | |
| scroll | scrollFocusCycling | passes | |
| scroll | scrollFocusWrapping | passes | |
| scroll | scrollSwapcolWrapping | passes | |
| scroll | scrollWindowRule | passes | |
| scroll | scroll_LAYOUT_HANDLED_fullscreen | passes | |
| scroll | scroll_LAYOUT_HANDLED_maximized | passes | |
| scroll | scroll_LAYOUT_HANDLED_TestFsingGroupedWindows | passes | |
| scroll | scroll_LAYOUT_HANDLED_fullscreenRetainsGeometryWhileScrolling | passes | |
| scroll | scroll_LAYOUT_HANDLED_floatingWindowHiding | differs | A floating Client under a fullscreen one stays visible and accepts input, and a second fullscreen Client ends the first one's fullscreen. allowedOverFullscreen is always false. |
| scroll | scroll_LAYOUT_HANDLED_layerVisibilityOnFs | not ported | The case opens a layer shell surface, which has no Windows meaning. |
| scroll | scroll_LAYOUT_HANDLED_focusInDirectionFocusFollowFocusTrue | passes | |
| scroll | scroll_LAYOUT_HANDLED_respectFocusFitMethodOnUnFs | passes | |
| scroll | scroll_LAYOUT_HANDLED_FloatingOntopFullscreenWorkspaceFocusRetention | passes | |
| scroll | scroll_DEFAULT_HANDLED_fullscreenMaximiseDispatchers | passes | |
| scroll | scroll_DEFAULT_HANDLED_TestFsingGroupedWindows | passes | |
| scroll | scroll_DEFAULT_HANDLED_testFsFocusUnderFSWindow | passes | |
| scroll | scroll_DEFAULT_HANDLED_newWindowTakesOverFullscreen | passes | |
| scroll | scroll_DEFAULT_HANDLED_exitWindowRetainsFullscreen | passes | |
| scroll | scroll_DEFAULT_HANDLED_FullscreenPinnedWindows | passes | |
| scroll | scroll_DEFAULT_HANDLED_FullscreenNonInterference | passes | |
| scroll | scroll_DEFAULT_HANDLED_FloatingOntopFullscreenWorkspaceFocusRetention | passes | |
| scroll | scroll_DEFAULT_HANDLED_posAndSizeSyncWhileFS | not ported | The case opens a layer shell surface, which has no Windows meaning. |
| scroll | scrollNegativeScaleGesture | differs | The scrollMove gesture action is a Gap. |
| scroll | testScrollingViewBehaviourDispatchFocusWindowFollowFocusFalse | passes | |
| scroll | testScrollingViewBehaviourDispatchFocusWindowFollowFocustrue | passes | |
| scroll | testScrollingViewBehaviourFocusFallback | passes | |
| scroll | testScrollingViewBehaviourFocusFallbackWithGroups | passes | |
| scroll | testScrollingViewBehaviourWorkspaceChange | passes | |
| scroll | testScrollingViewBehaviourSpecialWorkspaceChange | passes | |
| scroll | testScrollingViewBehaviourSpecialToSpecialWorkspaceChange | passes | |
| scroll | testScrollingViewBehaviourCloseWindowInGroup | passes | |
| scroll | testScrollingViewBehaviourMoveWindowIntoGroupFollowFocusFalse | passes | |
| scroll | testScrollingViewBehaviourMoveWindowInGroupFollowFocusTrue | passes | |
| scroll | testScrollingViewBehaviourNewLayer | not ported | The case opens a layer shell surface, which has no Windows meaning. |
| scroll | testScrollingViewBehaviourMoveFocusFollowFocusFalse | passes | |
| scroll | testScrollingViewBehaviourMoveFocusFollowFocusTrue | passes | |
| scroll | testScrollingViewBehaviourMoveFocusInGroupFollowFocusFalse | passes | |
| scroll | testScrollingViewBehaviourMoveFocusInGroupFollowFocusTrue | passes | |
| scroll | testScrollingViewBehaviourScheduledPropRefresh | passes | |
| scroll | testScrollInhibitor | passes | |
| scroll | layoutmsg_fit_into_view | passes | |
| scroll | layoutRuleExpand | passes | |
| scroll | scrollTapeOnClickOutOfWindow | not ported | The case soft-focuses a window with a test plugin call and clicks a pointer button, and Windows, not Winland, focuses a window on a click. |
| scroll | scrollTapeWithMouseClick | not ported | The case tells a button press from its release, and Windows, not Winland, focuses a window on a click. |
| scroll | properFocusBehvaior | passes | |
| window | swapWindow | passes | |
| window | windowGroupRules | passes | |
| window | windowFocusOnFullscreenConflict | passes | |
| window | focusFloatingOrTilingUnderFullscreen | passes | |
| window | windowMaximizeSize | passes | |
| window | floatingFocusOnFullscreen | not ported | Its only check is a call into the test plugin, which a core test cannot make. |
| window | groupFallbackFocus | passes | |
| window | bringActiveToTopMouseMovement | passes | |
| window | initialFloatSize | not ported | The result depends on the floating size kitty asks for, which a core test cannot match. |
| window | windowRuleFocusOnActivate | passes | |
| window | pinnedWorkspacesValid | passes | |
| window | windowruleWorkspaceEmpty | passes | |
| window | contentWindowRules | passes | |
| window | issue14038 | passes | |
| window | issue14134 | passes | |
| window | specialFloatRecenters | passes | |
| window | exactWindowSelectors | passes | |
| window | windows | passes | |
| window | cycle_nextTiled | passes | |
| window | cycle_nextFloating | passes | |
| window | discussion14233 | passes | |
| window | execRulesWorkspaceOverride | passes | |
| window | execRulesTagMutation | passes | |
| window | execRulesDescendantProcess | passes | |
| window | execRulesPendingIsolation | passes | |
| window | pinnedRetainsPositionOnWorkspaceChange | passes | |
| window | monitorrule | passes | |
| window | mouseResize | passes | |
| window | getFullscreenHandler | passes | |
| window | sendFsWindowToAnotherWorkspace | passes | |
| window | floatingForceOnscreen | passes | |
| window | specialWorkspacePinnedWindowUnmapFallback | passes | |
| window | windowRuleBorderColors | passes | |
| window | windowRuleBorderColorsFocus | passes | |
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
| groups | groupbarDisableWhenOnlyDimensions | passes | |
| groups | autoGroupFloatedIntoTiled | passes | |
| groups | autoGroupFloatedintoFloated | passes | |
| groups | autoGroupTiledIntoFloated | passes | |
| groups | groupedExitWindowRetainsFullscreen | passes | |
| groups | windowOpenInGroupReceivesCursorFocus | not ported | The case checks that kitty receives a real mouse click, which a core test cannot send. |
| moveintoorcreategroup | moveIntoOrCreateGroup | passes | |
| moveintoorcreategroup | crossMonitorGroupJoin | passes | |
| workspaces | workspacesCombined | passes | |
| workspaces | workspacesFollowProperNoGaps | passes | |
| workspaces | workspacesSmartGapsDirectionalMoveAcrossMonitors | passes | |
| workspaces | workspaceRenameChangeID | passes | |
| workspaces | workspaceRenameEmitsGlobalEvent | passes | |
| workspaces | windowAtExplicitWorkspace | not ported | The case calls a hit test on a hidden Workspace through the reference's test plugin, which no command reaches. |
| workspaces | workspaceChangeIDUpdatesRules | passes | |
| workspaces | workspaceRenameDoesNotChangeAddress | passes | |
| workspaces | workspaceRulesMatchBareAddressableNames | passes | |
| workspaces | luaGetWorkspace | not ported | The case reads workspace objects from the Lua API, which a line-based command cannot reach. |
| workspaces | luaSetWorkspace | not ported | The case sets workspaces through monitor objects of the Lua API, which a line-based command cannot reach. |
| workspaces | luaSetSpecialWorkspace | not ported | The case sets special workspaces through monitor objects of the Lua API, which a line-based command cannot reach. |
| workspaces | workspacesDistinctTiledAndFloatGaps | passes | |
| workspaces | workspaceSwitchUnfocusesWindowFromOldWorkspace | passes | |
| workspaces | luaFocusCreatesBareNamedWorkspace | passes | |
| persistent | persistentWorkspaces | passes | |
| persistent | workspaceLifecycleReleasesEmptyWorkspaces | passes | |
| monitor_rules | monitorMirrorAppliedWhenTargetAppears | not ported | The case checks which render passes a mirrored monitor draws, through the reference's test plugin. |
| monitor_rules | monitorBackgroundWorkspaceAndXpMode | not ported | The case checks which render passes a monitor's background draws, through the reference's test plugin. |
| master | focusMasterPrevious | passes | |
| master | masterTestFsFocusUnderFSWindow | passes | |
| master | masterFullscreenMaximiseDispatchers | passes | |
| master | masterNewWindowTakesOverFullscreen | passes | |
| master | masterExitWindowRetainsFullscreen | passes | |
| master | masterFullscreenPinnedWindows | passes | |
| master | masterFullscreenNonInterference | passes | |
| master | rollFocus | passes | |
| master | focusMasterClose | passes | |
| master | centerMasterColumnResize | passes | |
| master | masterCenterDropAtCursor | passes | |
| master | masterDropAtCursor | passes | |
| monocle | monocleWorkspaceRule | passes | |
| monocle | monocleFullscreenMaximiseDispatchers | passes | |
| monocle | monocleTestFsFocusUnderFSWindow | passes | |
| monocle | monocleNewWindowTakesOverFullscreen | passes | |
| monocle | monocleExitWindowRetainsFullscreen | passes | |
| monocle | monocleFullscreenPinnedWindows | passes | |
| monocle | monocleFullscreenNonInterference | passes | |
| layout | single_window_aspect_ratio | passes | |
| layout | crashOnGeomUpdate | differs | The monitor keyword refuses a mode and a scale, which are Gaps. |
| layout | layoutRefreshDuringMonitorTeardown | differs | The monitor keyword refuses a mode and a scale, which are Gaps. |
| layout | posPreserve | passes | |
| layout | focusMRUAfterClose | passes | |
| layout | focusPreservedLayoutChange | passes | |
| layout_custom | layoutCustomGrid | not ported | A custom layout is registered with hl.layout.register, which waits on the Lua config (ADR 0013). |
| layout_custom | layoutCustomColumns | not ported | A custom layout is registered with hl.layout.register, which waits on the Lua config (ADR 0013). |
| drag | dragTiledCentered | passes | |
| drag | dragTiledProportional | passes | |
| drag | dragFullscreenCentered | passes | |
| drag | dragFullscreenProportional | passes | |
| drag | dragLifecycleSignals | not ported | The case checks the signals of the drag controller inside the compositor through the test plugin, which the core has no counterpart for. |
| drag | dragThresholdSurvivesScrollEvents | passes | |
| snap | snap | passes | |
| focus | errorBarReservationFollowsFocusedMonitor | differs | seterror is not a request. |
| focus | crossMonitorFullscreenFocus | passes | |
| focus | silentMoveLastTiledWindowUnfocusesWindow | passes | |
| focus | crossMonitorEmptyWorkspaceUnfocusesWindow | passes | |
| follow_mouse_shrink | followMouseShrink | passes | |
| cursor | cursorPerOutputHyprcursor | not ported | The case checks the cursor image each output renders from a cursor theme, which a core test cannot see. |
| hyprctl | hyprctlDevicesActiveLayoutIndex | differs | input:kb_layout is a Gap, and there is no switchxkblayout or devices request. |
| hyprctl | hyprctlGetprop | passes | |
| hyprctl | hyprctlSubmap | passes | |
| hyprctl | hyprctlJsonErrors | passes | |
| hyprctl | hyprctlDescriptionsCurrentValues | passes | |
| hyprctl | hyprctlBindsJson | passes | |
| hyprctl | hyprctlWorkspaceJson | passes | |
| hyprctl | hyprctlREPL | not ported | It runs Lua code through repl and eval. The Lua config waits on ADR 0013. |
| hyprctl | hyprctlBatch | not ported | Its batch runs Lua code through repl. The Lua config waits on ADR 0013. |
| misc | misc | passes | |
| misc | processesThatDieEarlyAreReaped | not ported | It checks that the OS reaps a process the config starts, which a core test cannot see. |
| state | state | differs | Turning every monitor off leaves no monitor. The pinned release adds a FALLBACK monitor that keeps the Workspaces. |
| refactor_state | workspacePlacementActiveMove | passes | |
| refactor_state | workspacePlacementWindowState | passes | |
| refactor_state | globalWindowMoveState | passes | |
| refactor_state | pointerNoWarpsFocusesMonitorOnly | passes | |
| refactor_state | cursorWarpFocusOnMonitorChange | passes | |
| solitary | solitaryClients | not ported | Solitary, tearing and direct scanout are render paths, which a core test cannot see. |
| colors | monitorsColorManagement | differs | bitdepth and cm are Gaps. |
| animations | animationsTrivial | passes | |
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
| clients/child-window | childWindow | passes | |
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
| clients/xdg-activation | xdgActivationSerial | differs | With misc:focus_on_activate a window that takes the foreground keeps it, without the fresh input serial the pinned release requires. |
| clients/xdg-initial-maximize | xdgMaximizeBeforeInitialCommitIsIgnored | passes | |
| clients/xdg-initial-maximize | xdgMaximizeBeforeMapIsIgnored | passes | |
| clients/xdg-interactive | xdgInteractive | passes | |
| misc/plugin | plugin | passes | |
| misc/plugin | pluginVkb | not ported | Windows merges every keyboard into one key state and does not tell Winland which device a key came from. |
| exec | processSpawning | passes | |
| gestures | pinchDeltaScalePropagation | not ported | It checks a gesture the test plugin registers through the C++ plugin interface, which has no core form. |
| gestures | defaultSpecialWorkspaceGesture | passes | |
| gestures | live_gesture_callbacks | not ported | Gesture actions with start, update and end callbacks are Lua-only. |
| gestures | gestures | differs | Only the workspace and special gesture actions are honored. |
| keybinds | keybinds | differs | Per-device binds are not honored. |
| keybinds | orderedSubChordDeferral | passes | |
| keybinds | modifierReleaseBindShadowing | passes | |
| keybinds | luaDispatcherStrings | not ported | It reads the handler of a bind as a Lua value, which is Lua-only. |
| keyboard_input | pluginKeyboardEventHandler | not ported | It checks a keyboard event handler registered through the C++ plugin interface, which has no core form. |
| keyboard_input | orderedChordConsumption | passes | |
| keyboard_input | orderedChordRejectsReorderedPrefix | passes | |
| keyboard_input | orderedReleaseChord | passes | |
| keyboard_input | nonConsumingChordPreservesOrder | passes | |
| swallow | swallow | passes | |
| tags | tags | passes | |
| tags | tags_fullscreen_cleanup | passes | |
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
| special_workspace_swipe | specialWorkspaceSwipeThresholds | passes | |
| special_workspace_swipe | specialWorkspaceSwipeRestartAndDestructor | not ported | It destroys the gesture object mid-swipe through the test plugin, which core input cannot do. |
| special_workspace_swipe | specialWorkspaceSwipeReplacementInvalidation | passes | |
| special_workspace_swipe | specialWorkspaceSwipeMoveInvalidation | passes | |
| special_workspace_swipe | specialWorkspaceSwipeMoveExternalState | not ported | It sets the special workspace's alpha, offset and animation through the test plugin, which have no core form. |
| special_workspace_swipe | specialWorkspaceSwipeOutputRemoval | passes | |
| special_workspace_swipe | specialWorkspaceSwipeReentrantEnd | not ported | It starts a second swipe from inside the first one's end, through the test plugin, which core input cannot do. |
| special_workspace_swipe | specialWorkspaceSwipeEmptyWorkspaceLifetime | passes | |
