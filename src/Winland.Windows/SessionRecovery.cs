using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows;

internal static class SessionRecovery
{
    // Record process, class and title while the Client is alive. Recover matches
    // those later, so a reused window is not treated as the Client that left.
    internal static RecoverySnapshot Capture(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var clients = new List<RecoveredClient>(snapshot.Clients.Count);
        foreach (var client in snapshot.Clients)
        {
            if (client.Window.Value == 0)
                continue;

            var single = new RecoverySnapshot([client]);
            if (WindowIdentity.TryAnnotate(single, out var annotated) && annotated.Clients.Count == 1)
                clients.Add(annotated.Clients[0]);
        }

        return new RecoverySnapshot(clients);
    }

    // Show a visible Client before Recover reads it. A session switch can leave
    // it hidden, and a hidden window is not adopted again.
    internal static void Restore(WindowAdapter windows, RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Clients.Count == 0)
            return;

        ShowMatched(snapshot);
        windows.Recover(snapshot);
    }

    private static void ShowMatched(RecoverySnapshot snapshot)
    {
        foreach (var client in snapshot.Clients)
        {
            if (client.Hidden || client.ProcessId == 0 || !WindowIdentity.Matches(client))
                continue;

            var hwnd = new HWND((nint)client.Window.Value);
            if (PInvoke.IsIconic(hwnd))
                continue;

            _ = PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        }
    }
}
