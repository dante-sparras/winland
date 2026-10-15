namespace Winland;

public sealed partial class Core
{
    // Clients that asked for attention and have not been focused since, oldest first.
    private readonly List<WindowId> _urgent = [];

    private Outcome Flash(WindowId window)
    {
        if (Find(window) is null || _active == window)
            return View([]);

        MarkUrgent(window);
        return View([]);
    }

    private void MarkUrgent(WindowId window)
    {
        if (!_urgent.Contains(window))
            _urgent.Add(window);

        (_extraEvents ??= []).Add(new ClientUrgent(window));
    }

    private Outcome FocusUrgentOrLast(bool allowsElevation)
    {
        const string name = "focusurgentorlast";
        if ((UrgentClient() ?? LastClient()) is not WindowId target)
            return Rejected(name, "error: window not found", allowsElevation);

        _urgent.Remove(target);
        return FocusClient(target, name, allowsElevation);
    }

    private WindowId? UrgentClient()
    {
        foreach (var client in EveryClient())
        {
            if (client.Window != _active && _urgent.Contains(client.Window))
                return client.Window;
        }

        return null;
    }

    // The Client focused before the current one, or the last one focused when none is.
    private WindowId? LastClient()
    {
        var current = ActiveWindow();
        foreach (var window in _focusHistory)
        {
            if (window != current && Find(window) is not null)
                return window;
        }

        return null;
    }
}
