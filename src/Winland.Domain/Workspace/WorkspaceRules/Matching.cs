using System.Globalization;

namespace Winland.Domain;

internal static partial class WorkspaceRules
{
    private static bool Matches(
        string selector,
        WorkspaceId id,
        string? displayName,
        Monitor? monitor,
        Func<WindowCount, int>? count,
        FullscreenMode fullscreen)
    {
        selector = selector.Trim();
        if (selector.Length == 0)
            return true;

        if (IsPositive(selector, out var number))
            return id.TryGetNumber(out var current) && current == number;

        if (selector.StartsWith("name:", StringComparison.Ordinal))
            return false;

        if (selector == "special" || selector.StartsWith("special:", StringComparison.Ordinal))
            return selector != "special:" && id.IsSpecial && id.Name == (selector == "special" ? WorkspaceId.DefaultSpecial.Name : selector);

        if (!selector.Contains('[', StringComparison.Ordinal))
            return false;

        return MatchProperties(selector, id, displayName, monitor, count, fullscreen);
    }

    private static bool MatchProperties(
        string selector,
        WorkspaceId id,
        string? displayName,
        Monitor? monitor,
        Func<WindowCount, int>? count,
        FullscreenMode fullscreen)
    {
        for (var i = 0; i < selector.Length; i++)
        {
            if (char.IsWhiteSpace(selector[i]))
                continue;

            var close = selector.IndexOf(']', i);
            if (close < 0)
                return false;

            var token = selector[i..(close + 1)];
            i = close;
            var kind = token[0];
            if (token.Length < 3 || token[1] != '[' || token[^1] != ']')
                return false;

            var body = token[2..^1];
            switch (kind)
            {
                case 'r':
                    if (!TryRange(body, out var from, out var to) || !id.TryGetNumber(out var idNumber) || idNumber < from || idNumber > to)
                        return false;
                    break;
                case 's':
                    if (!TryBool(body, out var special) || special != id.IsSpecial)
                        return false;
                    break;
                case 'n':
                    if (!MatchNamed(body, id, displayName))
                        return false;
                    break;
                case 'm':
                    if (monitor is null || !MonitorRules.Matches(monitor, body))
                        return false;
                    break;
                case 'w':
                    if (!MatchCount(body, count))
                        return false;
                    break;
                case 'f':
                    if (!int.TryParse(body, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var wanted) || !MatchFullscreen(wanted, fullscreen))
                        return false;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    // As Winland's source tests it: -1 nothing covers the Workspace, 0 a fullscreen Client
    // covers it, whatever its client mode, and 1 a maximized one. Any other number tests nothing there.
    private static bool MatchFullscreen(int wanted, FullscreenMode covering) => wanted switch
    {
        -1 => covering == FullscreenMode.None,
        0 => covering == FullscreenMode.Fullscreen,
        1 => covering == FullscreenMode.Maximized,
        _ => true,
    };

    private static bool MatchNamed(string body, WorkspaceId id, string? displayName)
    {
        var name = displayName ?? id.Name;
        if (body.StartsWith("s:", StringComparison.Ordinal))
            return name.StartsWith(body[2..], StringComparison.Ordinal);

        if (body.StartsWith("e:", StringComparison.Ordinal))
            return name.EndsWith(body[2..], StringComparison.Ordinal);

        if (!TryBool(body, out var wantsNamed))
            return false;

        return wantsNamed == IsNamed(id);
    }

    // A Workspace is a number or a special workspace. n[true] matches neither.
    private static bool IsNamed(WorkspaceId id) => false;

    private static bool MatchCount(string body, Func<WindowCount, int>? counter)
    {
        var index = 0;
        bool? onlyTiled = null;
        var pinned = false;
        var groups = false;
        var visible = false;
        while (index < body.Length)
        {
            switch (body[index])
            {
                case 't' when onlyTiled is null:
                    onlyTiled = true;
                    break;
                case 'f' when onlyTiled is null:
                    onlyTiled = false;
                    break;
                case 'p' when !pinned:
                    pinned = true;
                    break;
                case 'g' when !groups:
                    groups = true;
                    break;
                case 'v' when !visible:
                    visible = true;
                    break;
                default:
                    goto Count;
            }

            index++;
        }

    Count:
        var count = counter?.Invoke(new WindowCount(onlyTiled, pinned, groups, visible)) ?? 0;
        var range = body[index..];
        if (range.Contains('-', StringComparison.Ordinal))
            return TryRange(range, out var from, out var to) && count >= from && count <= to;

        return int.TryParse(range, NumberStyles.None, CultureInfo.InvariantCulture, out var exact) && count == exact;
    }

    private static bool TryRange(string text, out int from, out int to)
    {
        from = 0;
        to = 0;
        var dash = text.IndexOf('-');
        if (dash <= 0 || dash == text.Length - 1)
            return false;

        return IsPositive(text[..dash], out from) && IsPositive(text[(dash + 1)..], out to) && to >= from;
    }

    private static string NameOf(string ident) => ident;
}
