using System.Globalization;
using System.Text.RegularExpressions;

namespace Winland;

/// <summary>
/// Resolves a window selector against Clients on any Workspace.
/// A match on the active window's Workspace wins when several Clients match.
/// </summary>
internal static class WindowSelector
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    internal static WindowId? Find(string selector, WindowId? active, IReadOnlyList<Client> clients)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(clients);

        var text = selector.Trim();
        if (text.Length == 0 || text.StartsWith("active", StringComparison.Ordinal))
            return active;

        if (text.StartsWith("floating", StringComparison.Ordinal))
            return First(floating: true, active, clients);

        if (text.StartsWith("tiled", StringComparison.Ordinal))
            return First(floating: false, active, clients);

        // A tag selector matches a Client when one of its tags matches the whole pattern, as in the pinned release.
        if (text.StartsWith("tag:", StringComparison.Ordinal))
        {
            var tagPattern = text[4..];
            return FirstMatch(active, clients, client => client.Tags is { } tags && tags.Any(tag => Matches(tag, tagPattern)));
        }

        if (text.StartsWith("stableid:", StringComparison.Ordinal))
            return null;

        if (text.StartsWith("address:", StringComparison.Ordinal))
        {
            var address = text[8..];
            return FirstMatch(active, clients, client => CommandProtocol.Address(client.Window) == address);
        }

        if (text.StartsWith("pid:", StringComparison.Ordinal))
        {
            var pid = text[4..];
            return FirstMatch(
                active,
                clients,
                client => client.ProcessId.ToString(CultureInfo.InvariantCulture) == pid);
        }

        var title = false;
        var initial = false;
        var pattern = text;
        if (text.StartsWith("initialtitle:", StringComparison.Ordinal))
        {
            title = true;
            initial = true;
            pattern = text[13..];
        }
        else if (text.StartsWith("title:", StringComparison.Ordinal))
        {
            title = true;
            pattern = text[6..];
        }
        else if (text.StartsWith("initialclass:", StringComparison.Ordinal))
        {
            initial = true;
            pattern = text[13..];
        }
        else if (text.StartsWith("class:", StringComparison.Ordinal))
        {
            pattern = text[6..];
        }

        return FirstMatch(active, clients, client => Matches(Field(client, title, initial), pattern));
    }

    private static string Field(Client client, bool title, bool initial)
    {
        if (title)
            return initial ? client.InitialTitle : client.Title;

        return initial ? client.InitialClass : client.Class;
    }

    private static WindowId? FirstMatch(WindowId? active, IReadOnlyList<Client> clients, Func<Client, bool> match)
    {
        WorkspaceId? home = null;
        if (active is WindowId focused)
        {
            foreach (var client in clients)
            {
                if (client.Window != focused)
                    continue;

                home = client.Workspace;
                break;
            }
        }

        WindowId? elsewhere = null;
        foreach (var client in clients)
        {
            if (!match(client))
                continue;

            if (home is WorkspaceId workspace && client.Workspace == workspace)
                return client.Window;

            elsewhere ??= client.Window;
        }

        return elsewhere;
    }

    private static WindowId? First(bool floating, WindowId? active, IReadOnlyList<Client> clients)
    {
        if (active is not WindowId focused)
            return null;

        WorkspaceId? workspace = null;
        foreach (var client in clients)
        {
            if (client.Window == focused)
            {
                workspace = client.Workspace;
                break;
            }
        }

        if (workspace is not WorkspaceId home)
            return null;

        foreach (var client in clients)
        {
            if (!client.Minimized && client.Floating == floating && client.Workspace == home)
                return client.Window;
        }

        return null;
    }

    private static bool Matches(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, $"\\A(?:{pattern})\\z", RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
