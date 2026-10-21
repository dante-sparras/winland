namespace Winland.Core;

public sealed partial class WindowManager
{
    // A window changed its class. The class it had when it appeared stays, for initialClass. Its Client takes the new
    // class, and the rules are matched against it again. The events follow from the new State.
    private Outcome Reclass(WindowClassChanged reclassed)
    {
        if (WorkspaceContaining(reclassed.Window) is not { } desk || desk.ClientOf(reclassed.Window) is not { } client || client.Class == reclassed.Class)
            return View([]);

        desk.Reclass(reclassed.Window, reclassed.Class);

        // A rule that matches the class may now match or stop matching, so the next pass reads every rule again.
        _matchedState = true;
        return View([]);
    }
}
