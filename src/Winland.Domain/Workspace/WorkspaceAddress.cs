using System.Globalization;

namespace Winland.Domain;

internal static class WorkspaceAddress
{
    internal readonly record struct Context(
        WorkspaceId Active,
        Func<WorkspaceId?> Previous,
        Func<WorkspaceId?> PreviousOnMonitor,
        IReadOnlyList<WorkspaceId> OnMonitor,
        IReadOnlyList<WorkspaceId> Open,
        Func<long, bool> NumberIsEmpty);

    internal readonly record struct Rule(WorkspaceId? Id, bool Silent);

    public static Rule ParseRule(string? raw, Context context)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new(null, false);

        var text = raw.Trim();
        var silent = false;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 && parts[^1] == "silent")
        {
            silent = true;
            text = string.Join(' ', parts[..^1]);
        }

        return TryResolve(text, context, out var id) ? new(id, silent) : new(null, false);
    }

    /// <summary>True when <paramref name="text"/> addresses a Workspace by a name instead of a number or a special workspace.</summary>
    public static bool IsNamed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        var space = text.IndexOfAny([' ', '\t']);
        if (space > 0)
            text = text[..space];

        if (text.StartsWith("name:", StringComparison.Ordinal))
            return true;

        if (text.Contains('[', StringComparison.Ordinal) || text.Contains(']', StringComparison.Ordinal))
            return false;

        if (text is "special" or "prev" or "previous" or "previous_per_monitor" or "next" || text.StartsWith("special:", StringComparison.Ordinal) || text.StartsWith("empty", StringComparison.Ordinal))
            return false;

        // A relative selector is one only when the offset is a number. r+notes is a name.
        if (text.Length > 2 && text[0] is 'r' or 'm' or 'e' && text[1] is '+' or '-' or '~' && IsSignedNumber(text[2..]))
            return false;

        if (text[0] is '+' or '-')
            return false;

        return !IsDigits(text);
    }

    public static bool TryResolve(string? text, Context context, out WorkspaceId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (TrySpecial(text, out id))
            return true;

        if (text.StartsWith("name:", StringComparison.Ordinal) || IsNamed(text))
            return false;

        if (text.StartsWith("empty", StringComparison.Ordinal))
            return TryEmpty(text, context, out id);

        if (text is "prev" or "previous" or "previous_per_monitor")
        {
            var perMonitor = text == "previous_per_monitor";
            // The history is read only for these words, so it is collected only when one is asked for.
            if ((perMonitor ? context.PreviousOnMonitor() : context.Previous()) is not WorkspaceId previous)
                return false;

            id = previous;
            return true;
        }

        if (text == "next")
        {
            if (!context.Active.TryGetNumber(out var current) || current == WorkspaceId.MaxNumber)
                return false;

            return TryNumber(current + 1, context, out id);
        }

        if (text.Contains('[', StringComparison.Ordinal) || text.Contains(']', StringComparison.Ordinal))
            return false;

        if (text.Length > 2 && text[0] is 'r' or 'm' or 'e' && text[1] is '+' or '-' or '~' && IsSignedNumber(text[2..]))
            return TrySelector(text, context, out id);

        if (text[0] is '+' or '-')
        {
            if (!context.Active.TryGetNumber(out var current) || !TryOffset(text, out var offset))
                return false;

            return TryNumber((long)current + offset, context, out id);
        }

        if (IsDigits(text))
            return IsNumber(text) && TryNumber(long.Parse(text, CultureInfo.InvariantCulture), context, out id);

        return false;
    }

    private static bool TrySpecial(string text, out WorkspaceId id)
    {
        id = default;
        if (text.Equals("special", StringComparison.Ordinal))
        {
            id = WorkspaceId.DefaultSpecial;
            return true;
        }

        const string Prefix = "special:";
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var name = text.Length > Prefix.Length ? text[Prefix.Length..] : "";
        id = name.Length == 0 || name == "special"
            ? WorkspaceId.DefaultSpecial
            : new WorkspaceId(Prefix + name);
        return true;
    }

    private static bool TryEmpty(string text, Context context, out WorkspaceId id)
    {
        id = default;
        var suffix = text[5..];
        var next = suffix.Contains('n', StringComparison.Ordinal);
        var start = 0L;
        if (next && context.Active.TryGetNumber(out var current))
            start = current;

        for (var number = start + 1; number < WorkspaceId.MaxNumber; number++)
        {
            if (!context.NumberIsEmpty(number))
                continue;

            return TryNumber(number, context, out id);
        }

        return false;
    }

    private static bool TrySelector(string text, Context context, out WorkspaceId id)
    {
        id = default;
        var kind = text[0];
        var absolute = text[1] == '~';
        if (!TryOffset(absolute ? text[2..] : text[1..], out var offset))
            return false;

        if (kind == 'r')
            return TryRelative(context, absolute, offset, out id);

        return TryCycle(context, allMonitors: kind == 'e', absolute, offset, out id);
    }

    // r walks the focused Monitor's numbers. r~N is that number, and r-1 on Workspace 1 stays there.
    private static bool TryRelative(Context context, bool absolute, int offset, out WorkspaceId id)
    {
        if (absolute)
            return TryNumber(offset, context, out id);

        if (!context.Active.TryGetNumber(out var current))
        {
            id = default;
            return false;
        }

        var next = (long)current + offset;
        if (next < 1)
            next = 1;

        return TryNumber(next, context, out id);
    }

    private static bool TryCycle(Context context, bool allMonitors, bool absolute, int offset, out WorkspaceId id)
    {
        id = default;
        var cycle = allMonitors ? context.Open : context.OnMonitor;
        if (cycle.Count == 0)
            return false;

        int current;
        if (absolute)
        {
            current = offset <= 0 ? 0 : Math.Min(offset, cycle.Count) - 1;
        }
        else
        {
            var size = cycle.Count;
            var remains = offset < 0 ? -((-offset) % size) : offset % size;
            current = -1;
            for (var i = 0; i < size; i++)
            {
                if (cycle[i] != context.Active)
                    continue;

                current = i;
                break;
            }

            current += remains;
            if (current >= size)
                current %= size;
            else if (current < 0)
                current += size;
        }

        id = cycle[current];
        return true;
    }

    private static bool TryOffset(string text, out int offset)
    {
        offset = 0;
        if (text.Length == 0)
            return false;

        var body = text[0] == '+' ? text[1..] : text;
        if (body.Length == 0 || !int.TryParse(body, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            return false;

        if (text[0] == '+' && value < 0)
            return false;

        offset = value;
        return true;
    }

    private static bool TryNumber(long number, Context context, out WorkspaceId id)
    {
        if (number < 1)
            number = 1;

        if (number > WorkspaceId.MaxNumber)
        {
            id = default;
            return false;
        }

        id = WorkspaceId.FromNumber(number, new MonitorId(context.Active.Monitor));
        return true;
    }

    private static bool IsNumber(string text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0;

    private static bool IsSignedNumber(string text) => IsDigits(text.StartsWith('-') ? text[1..] : text);

    private static bool IsDigits(string text)
    {
        if (text.Length == 0)
            return false;

        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return true;
    }
}
