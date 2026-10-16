using System.Globalization;

namespace Winland;

internal static class WorkspaceAddress
{
    internal readonly record struct Context(
        WorkspaceId Active,
        WorkspaceId? Previous,
        WorkspaceId? PreviousOnMonitor,
        IReadOnlyList<WorkspaceId> OnMonitor,
        IReadOnlyList<WorkspaceId> Open,
        Func<int, bool> NumberIsEmpty,
        Func<int, bool> NumberIsOnOtherMonitor);

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

    public static bool TryResolve(string? text, Context context, out WorkspaceId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        if (TrySpecial(text, out id))
            return true;

        if (text.StartsWith("name:", StringComparison.Ordinal))
        {
            var name = text[5..];
            if (name.Length == 0 || IsNumber(name))
                return false;

            id = new WorkspaceId(name);
            return true;
        }

        if (text.StartsWith("empty", StringComparison.Ordinal))
            return TryEmpty(text, context, out id);

        if (text is "prev" or "previous" or "previous_per_monitor")
        {
            var perMonitor = text == "previous_per_monitor";
            if ((perMonitor ? context.PreviousOnMonitor : context.Previous) is not WorkspaceId previous)
                return false;

            id = previous;
            return true;
        }

        if (text == "next")
        {
            if (!context.Active.TryGetNumber(out var current) || current == int.MaxValue)
                return false;

            id = WorkspaceId.FromNumber(current + 1);
            return true;
        }

        if (text.Contains('[', StringComparison.Ordinal) || text.Contains(']', StringComparison.Ordinal))
            return false;

        // As the pinned release's resolver reads it, text whose offset is not a number names a Workspace.
        if (text.Length > 2 && text[0] is 'r' or 'm' or 'e' && text[1] is '+' or '-' or '~' && IsSignedNumber(text[2..]))
            return TrySelector(text, context, out id);

        if (text[0] is '+' or '-')
        {
            if (!context.Active.TryGetNumber(out var current) || !TryOffset(text, out var offset))
                return false;

            return TryNumber((long)current + offset, out id);
        }

        if (IsDigits(text))
            return IsNumber(text) && TryNumber(int.Parse(text, CultureInfo.InvariantCulture), out id);

        id = new WorkspaceId(text);
        return true;
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
        var onMonitor = suffix.Contains('m', StringComparison.Ordinal);
        var next = suffix.Contains('n', StringComparison.Ordinal);
        var start = 0;
        if (next && context.Active.TryGetNumber(out var current))
            start = current;

        for (var number = start + 1; number < int.MaxValue; number++)
        {
            if (onMonitor && context.NumberIsOnOtherMonitor(number))
                continue;

            if (!context.NumberIsEmpty(number))
                continue;

            id = WorkspaceId.FromNumber(number);
            return true;
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

    private static bool TryRelative(Context context, bool absolute, int offset, out WorkspaceId id)
    {
        var named = Named(context.OnMonitor);
        return absolute
            ? TryAbsoluteRelative(context, offset, named, out id)
            : TrySignedRelative(context, offset, named, out id);
    }

    private static bool TryAbsoluteRelative(Context context, int offset, List<WorkspaceId> named, out WorkspaceId id)
    {
        id = default;
        if (offset <= 0)
            return false;

        var remains = offset - 1;
        if (remains < named.Count)
        {
            id = named[remains];
            return true;
        }

        remains -= named.Count;
        var number = 0;
        while (remains >= 0)
        {
            if (number == int.MaxValue)
                return false;

            number++;
            if (!context.NumberIsOnOtherMonitor(number))
                remains--;
        }

        return TryNumber(number, out id);
    }

    private static bool TrySignedRelative(Context context, int offset, List<WorkspaceId> named, out WorkspaceId id)
    {
        id = default;
        if (!context.Active.TryGetNumber(out var activeNumber))
        {
            var index = named.FindIndex(workspace => workspace == context.Active);
            if (index < 0)
                return false;

            var next = index + offset;
            if (next < 0)
                next = 0;

            if (next < named.Count)
            {
                id = named[next];
                return true;
            }

            var diff = next - (named.Count - 1);
            return Walk(context, diff, CountTaken(context, 1, diff), positive: true, named, out id);
        }

        var predictedLong = Math.Max(0L, (long)activeNumber + offset);
        if (predictedLong > int.MaxValue)
            return false;

        var predicted = (int)predictedLong;
        var positive = offset >= 0;
        var begin = positive ? activeNumber + 1 : predicted;
        var end = positive ? predicted : activeNumber;
        return Walk(context, predicted, CountTaken(context, begin, end), positive, named, out id);
    }

    private static bool Walk(
        Context context,
        int predicted,
        int remaining,
        bool positive,
        List<WorkspaceId> named,
        out WorkspaceId id)
    {
        id = default;
        var final = predicted;
        if (!positive)
        {
            var beginId = final;
            var current = final;
            while (remaining > 0 && current > 1)
            {
                current--;
                if (!context.NumberIsOnOtherMonitor(current))
                    remaining--;

                final = current;
            }

            if (final <= 0 || context.NumberIsOnOtherMonitor(final))
            {
                if (named.Count > 0)
                {
                    id = named[Math.Clamp(named.Count - remaining, 0, named.Count - 1)];
                    return true;
                }

                positive = true;
                final = beginId;
                remaining = 1;
            }
        }

        if (positive)
        {
            var current = final;
            while (remaining > 0 && current < int.MaxValue - 1)
            {
                current++;
                if (!context.NumberIsOnOtherMonitor(current))
                    remaining--;

                final = current;
            }
        }

        return TryNumber(final, out id);
    }

    private static int CountTaken(Context context, int begin, int end)
    {
        if (end < begin)
            return 0;

        var count = 0;
        for (var number = Math.Max(begin, 1); number <= end; number++)
        {
            if (context.NumberIsOnOtherMonitor(number))
                count++;

            if (number == int.MaxValue)
                break;
        }

        return count;
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

    private static List<WorkspaceId> Named(IReadOnlyList<WorkspaceId> cycle)
    {
        var named = new List<WorkspaceId>();
        foreach (var workspace in cycle)
        {
            if (!workspace.TryGetNumber(out _))
                named.Add(workspace);
        }

        return named;
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

    private static bool TryNumber(long number, out WorkspaceId id)
    {
        if (number < 1)
            number = 1;

        if (number > int.MaxValue)
        {
            id = default;
            return false;
        }

        id = WorkspaceId.FromNumber((int)number);
        return true;
    }

    private static bool IsNumber(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0;

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
