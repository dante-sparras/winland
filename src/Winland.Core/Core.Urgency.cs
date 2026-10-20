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

    // focuscurrentorlast focuses the Client focused before the newest one in the focus history, as the pinned release does.
    private Outcome FocusCurrentOrLast(bool allowsElevation)
    {
        const string name = "focuscurrentorlast";
        var live = _focusHistory.Where(window => Find(window) is not null).ToList();
        if (live.Count <= 1)
            return Rejected(name, "error: History too short", allowsElevation);

        return FocusClient(live[1], name, allowsElevation);
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

    // The Client the pinned release takes from its focus history, which it reads from the oldest end: with a Client
    // focused the second oldest, and with none the oldest. That is the one focused before the current one only while
    // two Clients have been focused.
    private WindowId? LastClient()
    {
        var oldestFirst = new List<WindowId>(_focusHistory.Count);
        for (var i = _focusHistory.Count - 1; i >= 0; i--)
        {
            if (Find(_focusHistory[i]) is not null)
                oldestFirst.Add(_focusHistory[i]);
        }

        var index = ActiveWindow() is null ? 0 : 1;
        return index < oldestFirst.Count ? oldestFirst[index] : null;
    }
}
