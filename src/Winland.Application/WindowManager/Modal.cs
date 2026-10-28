namespace Winland.Application;

// Modal windows. Windows disables a window while a modal window it owns is open, as a dialog box does, so a disabled
// window that owns a Client is shadowed by a modal and takes no focus.
public sealed partial class WindowManager
{
    // The owner each Client was adopted with, and the windows Windows has disabled.
    private readonly Dictionary<WindowId, WindowId> _owners = [];
    private readonly HashSet<WindowId> _disabled = [];

    private bool ShadowedByModal(WindowId window) => _disabled.Contains(window) && _owners.ContainsValue(window);

    private void NoteOwner(WindowAppeared appeared)
    {
        if (appeared.Owner is WindowId owner && owner != appeared.Window)
            _owners[appeared.Window] = owner;

        if (appeared.Disabled)
            _disabled.Add(appeared.Window);
    }

    // A dialog whose owner closed has no owner left, and so is no longer modal.
    private void ForgetOwner(WindowId window)
    {
        _owners.Remove(window);
        foreach (var owned in _owners.Where(pair => pair.Value == window).Select(pair => pair.Key).ToArray())
            _owners.Remove(owned);

        _disabled.Remove(window);
    }

    // Every outcome is decorated again, so the dim follows the change. A dialog whose owner was enabled or disabled
    // is modal or stops being modal.
    private Outcome ChangeEnabled(WindowEnabledChanged changed)
    {
        if (changed.Enabled)
            _disabled.Remove(changed.Window);
        else
            _disabled.Add(changed.Window);

        return View([]);
    }

}
