namespace Winland.Application;

/// <summary>Urgent Clients, and <c>focusurgentorlast</c>.</summary>
public sealed partial class WindowManager
{
    // Clients that asked for attention and have not been focused since, oldest first.
    private readonly List<WindowId> _urgent = [];

    private Outcome Flash(WindowId window)
    {
        if (Find(window) is null || _focus.Focused == window)
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

    private Outcome FocusUrgentOrLast()
    {
        if ((UrgentClient() ?? LastClient()) is not WindowId target)
            return Rejected("error: window not found");

        _urgent.Remove(target);
        return FocusClient(target);
    }

    private WindowId? UrgentClient()
    {
        foreach (var client in EveryClient())
        {
            if (client.Window != _focus.Focused && _urgent.Contains(client.Window))
                return client.Window;
        }

        return null;
    }

    // The Client Winland takes from its focus history, which it reads from the oldest end: with a Client
    // focused the second oldest, and with none the oldest. That is the one focused before the current one only while
    // two Clients have been focused.
    private WindowId? LastClient()
    {
        var oldestFirst = new List<WindowId>(_focus.History.Count);
        for (var i = _focus.History.Count - 1; i >= 0; i--)
        {
            if (Find(_focus.History[i]) is not null)
                oldestFirst.Add(_focus.History[i]);
        }

        var index = ActiveWindow() is null ? 0 : 1;
        return index < oldestFirst.Count ? oldestFirst[index] : null;
    }
}
