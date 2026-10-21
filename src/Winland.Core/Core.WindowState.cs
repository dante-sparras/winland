namespace Winland;

// Per-window state the core keeps outside the Layouts. Closing a window forgets it in all of it at once.
public sealed partial class Core
{
    private readonly WindowRegistry _windowState = new();

    // Every map, set and list keyed by a window, and every field that names one, is registered here.
    // _active is left out on purpose: Close moves it to the next Client itself. _painted and
    // _concealedMinimized are rebuilt from each State and need no forgetting. A test fails when anything
    // else keeps a closed window.
    private void TrackWindowState()
    {
        _windowState.Track(_focusRules);
        _windowState.Track(_awaitingMapFocus);
        _windowState.Track(_focusHistory);
        _windowState.Track(_urgent);
        _windowState.Track(ForgetFocusMarks);
        _windowState.Track(_opacity);
        _windowState.Track(_restored);
        _windowState.Track(_mapPlaced);
        _windowState.Track(_closeableAt);
        _windowState.Track(_cursorOnWindow);
        _windowState.Track(_pinned);
        _windowState.Track(_stacking);
        _windowState.Track(_pinFullscreened);
        _windowState.Track(_restoreClientMaximized);
        _windowState.Track(_overFullscreen);
        _windowState.Track(_hidingSeen);
        _windowState.Track(_belowFullscreen);
        _windowState.Track(_groupRules);
        _windowState.Track(ForgetDrag);
        _windowState.Track(_setProps);
        _windowState.Track(_ruleProps);
        _windowState.Track(_executables);
        _windowState.Track(_ruleSubjects);
        _windowState.Track(_staticTags);
        _windowState.Track(_ruleTags);
        _windowState.Track(_content);
        _windowState.Track(_shownMaximized);
    }
}
