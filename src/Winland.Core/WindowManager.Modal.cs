namespace Winland.Core;

// Modal windows. Windows disables a window while a modal window it owns is open, as a dialog box does, so a disabled
// window that owns a Client is shadowed by a modal, and that Client is modal. Winland dims the shadowed
// window with decoration:dim_modal, and with general:modal_parent_blocking it takes no focus.
public sealed partial class WindowManager
{
    // The owner each Client was adopted with, and the windows Windows has disabled.
    private readonly Dictionary<WindowId, WindowId> _owners = [];
    private readonly HashSet<WindowId> _disabled = [];

    private bool ShadowedByModal(WindowId window) => _disabled.Contains(window) && _owners.ContainsValue(window);

    private bool IsModal(WindowId window) => _owners.TryGetValue(window, out var owner) && _disabled.Contains(owner);

    // A window shadowed by a modal takes no focus with general:modal_parent_blocking.
    private bool BlockedByModal(WindowId window) => _session.ModalParentBlocking && ShadowedByModal(window);

    private void NoteOwner(WindowAppeared appeared)
    {
        if (appeared.Owner is WindowId owner && owner != appeared.Window)
            _owners[appeared.Window] = owner;

        if (appeared.Disabled)
            _disabled.Add(appeared.Window);
    }

    private void ForgetOwner(WindowId window)
    {
        _owners.Remove(window);
        _disabled.Remove(window);
    }

    // Windows whose modal state changed during this input. The rules event is added with the others.
    private List<WindowId>? _modalRules;

    // Every outcome is decorated again, so the dim follows the change. A dialog whose owner was enabled or disabled
    // is modal or stops being modal, and its rules are read again.
    private Outcome ChangeEnabled(WindowEnabledChanged changed)
    {
        var before = ModalClients();
        if (changed.Enabled)
            _disabled.Remove(changed.Window);
        else
            _disabled.Add(changed.Window);

        foreach (var window in ModalClients())
        {
            if (!before.Contains(window))
                (_modalRules ??= []).Add(window);
        }

        foreach (var window in before)
        {
            if (!IsModal(window))
                (_modalRules ??= []).Add(window);
        }

        return View([]);
    }

    private List<WindowId> ModalClients()
    {
        var modal = new List<WindowId>();
        foreach (var desk in _workspaces)
        {
            foreach (var client in desk.Layout.Clients)
            {
                if (IsModal(client.Window))
                    modal.Add(client.Window);
            }

            foreach (var waiting in desk.WaitingClients)
            {
                if (IsModal(waiting.Window))
                    modal.Add(waiting.Window);
            }
        }

        return modal;
    }
}
