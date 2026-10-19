namespace Winland;

public sealed partial class Core
{
    // A window changed its title. The title it had when it appeared stays, for initialTitle. Its Client takes the new
    // title, the rules are matched against it again, and the groupbar and the events follow from the new State.
    private Outcome Retitle(WindowTitleChanged retitled)
    {
        if (WorkspaceContaining(retitled.Window) is not { } desk || desk.ClientOf(retitled.Window) is not { } client || client.Title == retitled.Title)
            return View([]);

        desk.Retitle(retitled.Window, retitled.Title);

        // A rule that matches the title may now match or stop matching, so the next pass reads every rule again.
        _matchedState = true;
        return View([]);
    }
}
