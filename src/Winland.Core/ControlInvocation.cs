using System.Text;

namespace Winland;

public static class ControlInvocation
{
    public const string Usage =
        """
        usage: winlandctl [-j] [-r] [-a] dispatch <name> [args]
               winlandctl [-j] [-r] [-a] clients
               winlandctl [-j] [-r] [-a] workspaces
               winlandctl [-j] [-r] [-a] workspacerules
               winlandctl [-j] [-r] [-a] monitors [all]
               winlandctl [-j] [-r] [-a] activeworkspace
               winlandctl [-j] [-r] [-a] activewindow
               winlandctl [-j] [-r] [-a] reload
               winlandctl events
               winlandctl [-j] [-r] [-a] binds
               winlandctl [-j] [-r] [-a] version
               winlandctl [-j] [-r] [-a] cursorpos
               winlandctl [-j] [-r] [-a] configerrors
               winlandctl [-j] [-r] [-a] getoption <name>
               winlandctl [-j] [-r] [-a] systeminfo
               winlandctl [-j] [-r] [-a] locked
               winlandctl [-j] [-r] [-a] layouts
               winlandctl [-j] [-r] [-a] kill
               winlandctl [-j] [-r] [-a] rollinglog
               winlandctl [-j] [-r] [-a] keyword <name> <value>
               winlandctl [-j] [-r] [-a] notify [args]
               winlandctl [-j] [-r] [-a] dismissnotify [args]
               winlandctl [-j] [-r] [-a] getprop <window> <property>
               winlandctl [-j] [-r] [-a] setprop <window> <property> <value>
               winlandctl plugin list
               winlandctl plugin load <path>
               winlandctl plugin unload <name>
               winlandctl plugin install <git-url> [revision]
               winlandctl plugin update [--force]
               winlandctl plugin remove <git-url|name>
               winlandctl plugin list --installed
               winlandctl [-j] [-r] [-a] --batch <commands>

        """;

    public static bool TryCreate(string[] args, out string line, out bool events)
    {
        ArgumentNullException.ThrowIfNull(args);
        line = "";
        events = false;
        var json = false;
        var relayout = false;
        var all = false;
        string? batch = null;
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                for (var j = i + 1; j < args.Length; j++)
                    rest.Add(args[j]);
                break;
            }

            if (arg.StartsWith('-') && !IsNumber(arg))
            {
                if (arg == "-j")
                {
                    json = true;
                    continue;
                }

                if (arg == "-r")
                {
                    relayout = true;
                    continue;
                }

                if (arg == "-a")
                {
                    all = true;
                    continue;
                }

                if (arg == "--batch")
                {
                    if (batch is not null || i + 1 >= args.Length)
                        return false;

                    batch = args[++i];
                    continue;
                }

                return false;
            }

            rest.Add(arg);
        }

        var flags = "";
        if (json)
            flags += "j";
        if (relayout)
            flags += "r";
        if (all)
            flags += "a";

        if (batch is not null)
        {
            if (rest.Count > 0)
                return false;

            line = "[[BATCH]]" + (flags.Length == 0 ? batch : Inject(batch, flags));
            return true;
        }

        if (rest.Count == 0)
            return false;

        if (rest[0].Equals("events", StringComparison.OrdinalIgnoreCase) && rest.Count == 1)
        {
            events = true;
            line = "events";
            return true;
        }

        var body = Body(rest);
        if (body is null)
            return false;

        line = flags.Length == 0 ? body : flags + "/" + body;
        return true;
    }

    private static string? Body(List<string> args)
    {
        if (args[0].Equals("dispatch", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 2)
                return null;

            return "dispatch " + string.Join(' ', args.Skip(1));
        }

        if (args[0].Equals("monitors", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count == 1)
                return "monitors";

            if (args.Count == 2 && args[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                return "monitors all";

            return null;
        }

        if (args[0].Equals("plugin", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count == 2 && args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
                return "plugin list";

            if (args.Count >= 3 && args[1].Equals("load", StringComparison.OrdinalIgnoreCase))
                return "plugin load " + string.Join(' ', args.Skip(2));

            if (args.Count >= 3 && args[1].Equals("unload", StringComparison.OrdinalIgnoreCase))
                return "plugin unload " + string.Join(' ', args.Skip(2));

            return null;
        }

        if (TakesArguments(args[0]))
            return args[0].ToLowerInvariant() + (args.Count == 1 ? "" : " " + string.Join(' ', args.Skip(1)));

        if (args.Count != 1 || !IsBare(args[0]))
            return null;

        return args[0].ToLowerInvariant();
    }

    private static bool TakesArguments(string verb) =>
        verb.Equals("getoption", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("keyword", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("getprop", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("setprop", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("notify", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("seterror", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("dismissnotify", StringComparison.OrdinalIgnoreCase);

    private static bool IsBare(string verb) =>
        verb.Equals("clients", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("workspaces", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("workspacerules", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("activeworkspace", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("activewindow", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("reload", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("binds", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("version", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("cursorpos", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("configerrors", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("systeminfo", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("locked", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("layouts", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("submap", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("animations", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("descriptions", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("kill", StringComparison.OrdinalIgnoreCase)
        || verb.Equals("rollinglog", StringComparison.OrdinalIgnoreCase);

    private static string Inject(string payload, string flags)
    {
        var prefix = flags + "/";
        var result = new StringBuilder(prefix);
        var bracket = 0;
        for (var i = 0; i < payload.Length; i++)
        {
            var ch = payload[i];
            if (ch == '[')
                bracket++;
            else if (ch == ']')
                bracket--;

            result.Append(ch);
            if (ch != ';' || bracket != 0)
                continue;

            while (i + 1 < payload.Length && char.IsWhiteSpace(payload[i + 1]))
                i++;

            result.Append(prefix);
        }

        return result.ToString();
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
