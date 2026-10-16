using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winland.Windows;

public static class WindowIdentity
{
    public static bool Matches(RecoveredClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return TryRead(client.Window, out var processId, out var windowClass, out var title)
            && processId == client.ProcessId
            && windowClass == client.Class
            && title == client.Title;
    }

    /// <summary>Records process, class and title of each Client that is still open. A window that is gone is left out.</summary>
    public static RecoverySnapshot Annotate(RecoverySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var clients = new List<RecoveredClient>(snapshot.Clients.Count);
        foreach (var client in snapshot.Clients)
        {
            if (!TryRead(client.Window, out var processId, out var windowClass, out var title))
                continue;

            clients.Add(client with
            {
                Class = windowClass,
                Title = title,
                ProcessId = processId,
            });
        }

        return snapshot with { Clients = clients };
    }

    private static bool TryRead(WindowId window, out long processId, out string windowClass, out string title)
    {
        processId = 0;
        windowClass = "";
        title = "";
        var hwnd = new HWND((nint)window.Value);
        if (hwnd.IsNull || !PInvoke.IsWindow(hwnd))
            return false;

        if (PInvoke.GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid == 0)
            return false;

        processId = pid;
        windowClass = WindowAdapter.ClassName(hwnd);
        title = WindowAdapter.WindowTitle(hwnd);
        return true;
    }
}
