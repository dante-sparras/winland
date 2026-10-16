using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winland.Windows;

public static class WindowIdentity
{
    public static bool Matches(RecoveredClient client) => Recognize(client) == Recognition.SameClient;

    /// <summary>How the window open now at a recorded Client's handle relates to that Client.</summary>
    internal static Recognition Recognize(RecoveredClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return TryRead(client.Window, out var processId, out var windowClass, out var title)
            ? Recognize(client, processId, windowClass, title)
            : Recognition.Other;
    }

    // Process and class tell the same window from a reused handle. The title also has to match before the
    // window rejoins its Workspace, because a title is the only other sign that it is the same Client.
    internal static Recognition Recognize(RecoveredClient client, long processId, string windowClass, string title)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (processId != client.ProcessId || windowClass != client.Class)
            return Recognition.Other;

        return title == client.Title ? Recognition.SameClient : Recognition.SameWindow;
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

    internal enum Recognition
    {
        /// <summary>Another window, or none.</summary>
        Other,

        /// <summary>The same window, which may have changed since. What the previous run left on it is put back.</summary>
        SameWindow,

        /// <summary>The same window, sure enough to rejoin its Workspace.</summary>
        SameClient,
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
