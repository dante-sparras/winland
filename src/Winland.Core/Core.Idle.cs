namespace Winland;

public sealed partial class Core
{
    private readonly Dictionary<WindowId, IdleInhibitMode> _idle = [];

    private void RememberIdle(WindowId window, WindowDecision decision)
    {
        if (decision.IdleInhibit == IdleInhibitMode.None)
            _idle.Remove(window);
        else
            _idle[window] = decision.IdleInhibit;
    }

    // Always holds while the Client lives. Focus holds while it is the focused Client, and
    // fullscreen while it is fullscreen or maximized on a shown Workspace.
    private bool InhibitsIdle(Client client, bool shown, WindowId? active)
    {
        return IdleModeOf(client.Window) switch
        {
            IdleInhibitMode.Always => true,
            IdleInhibitMode.Focus => client.Window == active,
            IdleInhibitMode.Fullscreen => client.Fullscreen != FullscreenMode.None && shown,
            _ => false,
        };
    }
}
