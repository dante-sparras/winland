namespace Winland.Core;

/// <summary>The click <c>kill</c> waits for, at <paramref name="Position"/>, or Escape when it is null.</summary>
public sealed record ClickKillChosen(Point? Position) : Event;

/// <summary><c>kill&gt;&gt;ADDRESS</c>: click to kill ended the window's program.</summary>
public sealed record ClientKilled(WindowId Window) : StreamEvent;

public sealed partial class WindowManager
{
    private int _clickKill;

    /// <summary>Whether <c>kill</c> waits for a click. The hooks read it with no lock, and keep that click from the window.</summary>
    public bool ClickKillArmed => Volatile.Read(ref _clickKill) != 0;

    private CommandReply ArmClickKill()
    {
        Volatile.Write(ref _clickKill, 1);
        return new CommandReply("ok", View([]));
    }

    // The next click ends the program of the Client under it, as Winland's kill mode does, and Escape gives up.
    // Either way the mode ends.
    private Outcome ChooseKill(ClickKillChosen chosen)
    {
        Volatile.Write(ref _clickKill, 0);
        if (chosen.Position is not Point at
            || WindowUnder(at, shrink: false) is not WindowId window
            || Find(window) is not Client client
            || client.ProcessId <= 0)
            return View([]);

        (_extraEvents ??= []).Add(new ClientKilled(window));
        return View([]) with { Kill = new KillRequest(client.ProcessId) { Window = window } };
    }
}
