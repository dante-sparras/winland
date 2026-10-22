namespace Winland.Application;

/// <summary>Turns <c>winland</c>'s arguments into the line it sends down the pipe.</summary>
public static class ControlInvocation
{
    // The verbs come from CommandProtocol, so a verb the pipe answers is one winland takes.
    public static string Usage { get; } =
        string.Concat(CommandProtocol.VerbForms.Select(form => "       winland " + (form == "events" ? "" : "[-j] [-r] [-a] ") + form + "\n"))
        + "       winland [-j] [-r] [-a] --batch <commands>\n";

    /// <summary>
    /// The line for <paramref name="args"/>: flags such as <c>-j</c> become <c>j/</c>, and the rest is joined with
    /// spaces. False when the line is not a request the pipe answers. <paramref name="events"/> is true for
    /// <c>events</c>, which follows the Event stream.
    /// </summary>
    public static bool TryCreate(string[] args, out string line, out bool events)
    {
        ArgumentNullException.ThrowIfNull(args);
        line = "";
        events = false;
        var flags = "";
        string? batch = null;
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                rest.AddRange(args.Skip(i + 1));
                break;
            }

            if (!arg.StartsWith('-') || IsNumber(arg))
            {
                rest.Add(arg);
                continue;
            }

            if (arg is "-j" or "-r" or "-a")
            {
                if (!flags.Contains(arg[1], StringComparison.Ordinal))
                    flags += arg[1];

                continue;
            }

            if (arg != "--batch" || batch is not null || i + 1 >= args.Length)
                return false;

            batch = args[++i];
        }

        var prefix = flags.Length == 0 ? "" : flags + "/";
        if (batch is not null)
        {
            if (rest.Count > 0)
                return false;

            line = prefix + "[[BATCH]]" + batch;
            return true;
        }

        if (rest.Count == 0 || CommandProtocol.Parse(prefix + string.Join(' ', rest)) is not CommandParsed parsed)
            return false;

        line = prefix + string.Join(' ', rest);
        events = parsed.Request is EventsRequest;
        return true;
    }

    // A negative number is an argument, not a flag: "-20", "-0.1", and "-50," as the
    // first of two numbers, the way the reference control tool reads them.
    private static bool IsNumber(string text)
    {
        if (text.EndsWith(','))
            text = text[..^1];

        var i = 0;
        if (text.Length > 1 && text[0] == '-')
            i = 1;

        if (i >= text.Length || !char.IsAsciiDigit(text[^1]))
            return false;

        var point = false;
        for (; i < text.Length; i++)
        {
            if (text[i] == '.' && !point)
            {
                point = true;
                continue;
            }

            if (!char.IsAsciiDigit(text[i]))
                return false;
        }

        return true;
    }
}
