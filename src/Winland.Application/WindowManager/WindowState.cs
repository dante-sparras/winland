namespace Winland.Application;

// Per-window state the core keeps outside the Layouts. Closing a window forgets it in all of it at once.
public sealed partial class WindowManager
{
    private readonly WindowRegistry _windowState = new();

    // Every map, set and list keyed by a window, and every field that names one, is registered here.
    // The Focused client is left out on purpose: Close moves it to the next Client itself. _painted and
    // _concealedMinimized are rebuilt from each State and need no forgetting. A test fails when anything
    // else keeps a closed window.
    private void TrackWindowState()
    {
        _windowState.Track(_props.Forget);
        _windowState.Track(_renamed);
        _windowState.Track(_awaitingMapFocus);
        _windowState.Track(_urgent);
        _windowState.Track(ForgetFocusMarks);
        _windowState.Track(_restored);
        _windowState.Track(_mapPlaced);
        _windowState.Track(_cursorOnWindow);
        _windowState.Track(_pins.Forget);
        _windowState.Track(_stacking);
        _windowState.Track(_cover.Forget);
        _windowState.Track(ForgetDrag);
        _windowState.Track(ForgetOwner);
        _windowState.Track(_shownMaximized);
        _windowState.Track(_initialWorkspaces.Closed);
    }
}
