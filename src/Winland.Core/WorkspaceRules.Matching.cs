using System.Globalization;
using System.Text.Json.Serialization;

namespace Winland;

internal static partial class WorkspaceRules
{
    private static bool Matches(
        string selector,
        WorkspaceId id,
        string? displayName,
        Monitor? monitor,
        int windows,
        int tiled,
        int floating,
        FullscreenMode fullscreen)
    {
        selector = selector.Trim();
        if (selector.Length == 0)
            return true;

        if (IsPositive(selector, out var number))
            return id.TryGetNumber(out var current) && current == number;

        if (selector.StartsWith("name:", StringComparison.Ordinal))
        {
            var name = selector[5..];
            return name.Length > 0 && (name == id.Name || name == displayName);
        }

        if (!selector.Contains('[', StringComparison.Ordinal))
            return false;

        return MatchProperties(selector, id, displayName, monitor, windows, tiled, floating, fullscreen);
    }

    private static bool MatchProperties(
        string selector,
        WorkspaceId id,
        string? displayName,
        Monitor? monitor,
        int windows,
        int tiled,
        int floating,
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
                    if (!MatchCount(body, windows, tiled, floating))
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

    // As the pinned release's source tests it: -1 nothing covers the Workspace, 0 a fullscreen Client
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

        var named = !id.IsSpecial && !id.TryGetNumber(out _);
        return wantsNamed == named;
    }

    private static bool MatchCount(string body, int windows, int tiled, int floating)
    {
        var index = 0;
        int? onlyTiled = null;
        var pinned = false;
        var groups = false;
        while (index < body.Length)
        {
            switch (body[index])
            {
                case 't' when onlyTiled is null:
                    onlyTiled = 1;
                    break;
                case 'f' when onlyTiled is null:
                    onlyTiled = 0;
                    break;
                case 'p' when !pinned:
                    pinned = true;
                    break;
                case 'g' when !groups:
                    groups = true;
                    break;
                case 'v':
                    break;
                default:
                    goto Count;
            }

            index++;
        }

    Count:
        var count = groups || pinned ? 0 : onlyTiled switch
        {
            1 => tiled,
            0 => floating,
            _ => windows,
        };
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

    private static string NameOf(string ident)
    {
        if (ident.StartsWith("name:", StringComparison.Ordinal))
            return ident[5..];

        return IsPositive(ident, out _) ? ident : ident;
    }
}
