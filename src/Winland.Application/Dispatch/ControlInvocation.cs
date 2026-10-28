namespace Winland.Application;

/// <summary>Turns <c>winland</c>'s arguments into the line it sends down the pipe.</summary>
public static class ControlInvocation
{
    // The verbs come from CommandProtocol, so a verb the pipe answers is one winland takes.
    public static string Usage { get; } =
        string.Concat(CommandProtocol.VerbForms.Select(form => "       winland " + (form == "events" ? "" : "[-j] ") + form + "\n"))
        + "       winland [-j] --batch <commands>\n";

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

        // The request is one line, so an argument with a line break in it would be cut there.
        if (args.Any(arg => arg.Contains('\n', StringComparison.Ordinal) || arg.Contains('\r', StringComparison.Ordinal)))
            return false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // The words after "dispatch exec" are a command line, so "dispatch exec wt -p pwsh" keeps "-p" and
            // "dispatch exec wsl -- ls" keeps "--". Elsewhere a dash that is not a number is a flag, as the reference
            // control tool reads it.
            if (IsCommandLine(rest) || !arg.StartsWith('-') || IsNumber(arg))
            {
                rest.Add(arg);
                continue;
            }

            if (arg == "--")
            {
                rest.AddRange(args.Skip(i + 1));
                break;
            }

            if (arg is "-j")
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

        if (rest.Count == 0)
            return false;

        var request = string.Join(' ', Quoted(rest));
        if (CommandProtocol.Parse(prefix + request) is not CommandParsed parsed)
            return false;

        // The Event stream is plain text, so events takes no flag.
        events = parsed.Request is EventsRequest;
        if (events && prefix.Length > 0)
            return false;

        line = prefix + request;
        return true;
    }

    private static bool IsCommandLine(List<string> words) =>
        words.Count >= 2
        && words[0].Equals("dispatch", StringComparison.OrdinalIgnoreCase)
        && words[1].Equals("exec", StringComparison.OrdinalIgnoreCase);

    // The shell took the quotes off an argument with a space, such as a program under C:\Program Files. When the
    // command line exec runs is several words, such a word gets them back, so the program's path stays one argument.
    // A command line given as one word, such as "notepad C:\a b.txt" or "[float] notepad", is already whole.
    private static IEnumerable<string> Quoted(List<string> words)
    {
        var command = IsCommandLine(words) && words.Count > 3;
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            yield return command && i >= 2 && word.Contains(' ', StringComparison.Ordinal)
                && !word.Contains('"', StringComparison.Ordinal) && !word.StartsWith('[')
                ? "\"" + word + "\""
                : word;
        }
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
