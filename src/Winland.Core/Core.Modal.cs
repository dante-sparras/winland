namespace Winland;

// Modal windows. Windows disables a window while a modal window it owns is open, as a dialog box does, so a disabled
// window that owns a Client is shadowed by a modal, and that Client is modal. The pinned release dims the shadowed
// window with decoration:dim_modal, and with general:modal_parent_blocking it takes no focus.
public sealed partial class Core
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

    // Every outcome is decorated again, so the dim follows the change.
    private Outcome ChangeEnabled(WindowEnabledChanged changed)
    {
        if (changed.Enabled)
            _disabled.Remove(changed.Window);
        else
            _disabled.Add(changed.Window);

        return View([]);
    }
}
