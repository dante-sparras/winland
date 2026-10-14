# The elevated helper starts when an administrator window comes forward

The desktop hook is not called while an administrator window is in front, so the elevated helper's hook takes the Chords there. Windows can bring an administrator window forward itself: Ctrl+Shift+Esc does that to a Task Manager that already sits on its tile. Nothing Winoma asked for is refused then, so a helper that starts only when a place, a raise, a close, or a focus is refused would not run, and no Chord would work until another window is clicked.

So the window watch reports each window that takes the foreground, and the window already in front when Winland starts. An administrator window starts the helper unless it is running or was refused (`WindowExecutor.Front`). A Host with `administratorWindows` off, which is the default (ADR 0069), counts as refused, and the helper never starts there. The watch runs in `Winland.Host.exe` (ADR 0030).

On a Host where Windows asks for consent, the question comes when an administrator window first comes forward, not when one first has to be moved. It also comes for a dialog that is never tiled. It is asked once: a helper that exits before it has connected counts as a refusal too, and a refusal is kept until the desktop starts again.

The helper's hook is in place a few hundred milliseconds after the window comes forward. A Chord pressed before that goes to Windows.

The helper does what Windows refuses the desktop on an administrator window, each as a line on its pipe (`ElevatedBook`): it places, raises, closes, and focuses the window, and writes the Workspace mark on it or takes it off (ADR 0078). It hides nothing. Explorer cloaks an administrator window for the desktop itself, so such a window is hidden with its Workspace whether or not the helper runs (ADR 0003).

The helper is `Winland.Host.exe --place`. Winland starts it when an administrator window is in front, and tells it which chords it closes or forwards. The shell does not.
