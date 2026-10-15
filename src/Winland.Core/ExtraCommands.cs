using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Winland.OrdinalText;

namespace Winland;

public static class ExtraCommands
{
    public static bool TryParse(string text, CommandFlags flags, out CommandParse parse)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.StartsWith("[[BATCH]]", StringComparison.Ordinal))
        {
            parse = Parsed(new BatchRequest(text["[[BATCH]]".Length..]), flags);
            return true;
        }

        var verbEnd = IndexOfWhitespace(text);
        var verb = verbEnd < 0 ? text : text[..verbEnd];
        var rest = verbEnd < 0 ? "" : text[(verbEnd + 1)..].TrimStart();

        if (Is(verb, "binds"))
            return Exact(rest, new BindsRequest(), flags, out parse);
        if (Is(verb, "version"))
            return Exact(rest, new VersionRequest(), flags, out parse);
        if (Is(verb, "cursorpos"))
            return Exact(rest, new CursorPosRequest(), flags, out parse);
        if (Is(verb, "configerrors"))
            return Exact(rest, new ConfigErrorsRequest(), flags, out parse);
        if (Is(verb, "systeminfo"))
            return Exact(rest, new SystemInfoRequest(), flags, out parse);
        if (Is(verb, "locked"))
            return Exact(rest, new LockedRequest(), flags, out parse);
        if (Is(verb, "layouts"))
            return Exact(rest, new LayoutsRequest(), flags, out parse);
        if (Is(verb, "kill"))
            return Exact(rest, new ClickKillRequest(), flags, out parse);
        if (Is(verb, "rollinglog"))
            return Exact(rest, new RollingLogRequest(), flags, out parse);

        if (Is(verb, "getoption"))
        {
            parse = Parsed(new GetOptionRequest(First(rest)), flags);
            return true;
        }

        if (Is(verb, "keyword"))
        {
            SplitOnce(rest, out var name, out var value);
            parse = Parsed(new KeywordRequest(name, value), flags);
            return true;
        }

        if (Is(verb, "getprop"))
        {
            SplitProp(rest, withValue: false, out var selector, out var property, out _);
            parse = Parsed(new GetPropRequest(selector, property), flags);
            return true;
        }

        if (Is(verb, "setprop"))
        {
            SplitProp(rest, withValue: true, out var selector, out var property, out var value);
            parse = Parsed(new SetPropRequest(selector, property, value), flags);
            return true;
        }

        if (Is(verb, "notify"))
        {
            parse = Parsed(new NotifyRequest(), flags);
            return true;
        }

        if (Is(verb, "dismissnotify"))
        {
            parse = Parsed(new DismissNotifyRequest(), flags);
            return true;
        }

        parse = new CommandRejected("error: unknown request");
        return false;
    }

    public static IReadOnlyList<string> SplitBatch(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var commands = new List<string>();
        var bracket = 0;
        var start = 0;
        for (var i = 0; i <= payload.Length; i++)
        {
            var ch = i < payload.Length ? payload[i] : ';';
            if (ch == '[')
                bracket++;
            else if (ch == ']')
                bracket--;
            else if (ch == ';' && bracket == 0)
            {
                if (start < i)
                {
                    var command = payload[start..i].Trim();
                    if (command.Length > 0)
                        commands.Add(command);
                }

                start = i + 1;
            }
        }

        return commands;
    }

    public static string FormatBinds(IReadOnlyList<Bind> binds)
    {
        ArgumentNullException.ThrowIfNull(binds);
        var views = new BindJson[binds.Count];
        for (var i = 0; i < binds.Count; i++)
            views[i] = Describe(binds[i]);

        return JsonSerializer.Serialize(views);
    }

    public static string FormatVersion() =>
        JsonSerializer.Serialize(new VersionJson(
            "",
            BuildStamp.Commit,
            BuildStamp.Version,
            BuildStamp.Dirty,
            "",
            "",
            BuildStamp.Tag,
            "",
            []));

    public static string FormatCursor(Point point) =>
        string.Create(CultureInfo.InvariantCulture, $"{{\"x\":{point.X},\"y\":{point.Y}}}");

    public static string FormatLocked(bool locked) =>
        locked ? "{\"locked\":true}" : "{\"locked\":false}";

    public static string FormatLayouts() => JsonSerializer.Serialize(LayoutFactory.Names);

    public static string FormatErrors(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return JsonSerializer.Serialize(errors, Relaxed);
    }

    public static string FormatProp(string name, bool value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return $"{{\"{name}\":{(value ? "true" : "false")}}}";
    }

    public static string FormatSystem(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var text = new StringBuilder();
        text.Append("Winland ").Append(BuildStamp.Version).Append('\n');
        text.Append("OS: ").Append(Environment.OSVersion.VersionString).Append('\n');
        text.Append(".NET: ").Append(Environment.Version).Append('\n');
        text.Append("Monitors:");
        if (state.Screens.Count == 0)
        {
            text.Append(" none");
            return text.ToString();
        }

        foreach (var screen in state.Screens)
        {
            var monitor = screen.Monitor;
            text.Append('\n').Append(monitor.Name).Append(' ');
            text.Append(monitor.Width.ToString(CultureInfo.InvariantCulture));
            text.Append('x');
            text.Append(monitor.Height.ToString(CultureInfo.InvariantCulture));
            text.Append(" @ ");
            text.Append(monitor.X.ToString(CultureInfo.InvariantCulture));
            text.Append(',');
            text.Append(monitor.Y.ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    internal static int ModMask(Modifier modifiers)
    {
        var mask = 0;
        if (modifiers.HasFlag(Modifier.Shift))
            mask |= 1 << 0;
        if (modifiers.HasFlag(Modifier.Ctrl))
            mask |= 1 << 2;
        if (modifiers.HasFlag(Modifier.Alt))
            mask |= 1 << 3;
        if (modifiers.HasFlag(Modifier.Super))
            mask |= 1 << 6;

        return mask;
    }

    private static BindJson Describe(Bind bind)
    {
        var key = bind.Key;
        var keycode = 0;
        if (key.StartsWith("code:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key["code:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            && code >= 0)
        {
            key = "";
            keycode = code;
        }

        var mouse = bind.Flags.HasFlag(BindFlag.Mouse) || bind.Key.StartsWith("mouse:", StringComparison.OrdinalIgnoreCase);
        return new BindJson(
            bind.Flags.HasFlag(BindFlag.Locked),
            mouse,
            bind.Flags.HasFlag(BindFlag.Release),
            bind.Flags.HasFlag(BindFlag.Repeat),
            bind.Flags.HasFlag(BindFlag.LongPress),
            bind.Flags.HasFlag(BindFlag.NonConsuming),
            false,
            bind.Description is not null,
            ModMask(bind.Modifiers),
            bind.Submap ?? "",
            bind.Flags.HasFlag(BindFlag.SubmapUniversal) ? "true" : "false",
            key,
            keycode,
            bind.Key.Equals("catchall", StringComparison.Ordinal),
            bind.Description ?? "",
            false,
            bind.Dispatcher,
            bind.Arguments);
    }

    private static bool Exact(string rest, CommandRequest request, CommandFlags flags, out CommandParse parse)
    {
        if (rest.Length > 0)
        {
            parse = new CommandRejected("error: unknown request");
            return false;
        }

        parse = Parsed(request, flags);
        return true;
    }

    private static CommandParse Parsed(CommandRequest request, CommandFlags flags) =>
        new CommandParsed(request with { Flags = flags });

    private static string First(string rest)
    {
        var end = IndexOfWhitespace(rest);
        return end < 0 ? rest : rest[..end];
    }

    private static void SplitOnce(string rest, out string name, out string value)
    {
        var end = IndexOfWhitespace(rest);
        if (end < 0)
        {
            name = rest;
            value = "";
            return;
        }

        name = rest[..end];
        value = rest[(end + 1)..].TrimStart();
    }

    private static void SplitProp(string rest, bool withValue, out string selector, out string property, out string value)
    {
        selector = "";
        property = "";
        value = "";
        var first = IndexOfWhitespace(rest);
        if (first < 0)
        {
            selector = rest;
            return;
        }

        selector = rest[..first];
        var tail = rest[(first + 1)..].TrimStart();
        var second = IndexOfWhitespace(tail);
        if (second < 0)
        {
            property = tail;
            return;
        }

        property = tail[..second];
        if (withValue)
            value = tail[(second + 1)..].TrimStart();
    }

    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static int IndexOfWhitespace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }

        return -1;
    }

    private sealed record BindJson(
        [property: JsonPropertyName("locked")] bool Locked,
        [property: JsonPropertyName("mouse")] bool Mouse,
        [property: JsonPropertyName("release")] bool Release,
        [property: JsonPropertyName("repeat")] bool Repeat,
        [property: JsonPropertyName("longPress")] bool LongPress,
        [property: JsonPropertyName("non_consuming")] bool NonConsuming,
        [property: JsonPropertyName("auto_consuming")] bool AutoConsuming,
        [property: JsonPropertyName("has_description")] bool HasDescription,
        [property: JsonPropertyName("modmask")] int Modmask,
        [property: JsonPropertyName("submap")] string Submap,
        [property: JsonPropertyName("submap_universal")] string SubmapUniversal,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("keycode")] int Keycode,
        [property: JsonPropertyName("catch_all")] bool CatchAll,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("allow_input_capture")] bool AllowInputCapture,
        [property: JsonPropertyName("dispatcher")] string Dispatcher,
        [property: JsonPropertyName("arg")] string Arg);

    private sealed record VersionJson(
        [property: JsonPropertyName("branch")] string Branch,
        [property: JsonPropertyName("commit")] string Commit,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("dirty")] bool Dirty,
        [property: JsonPropertyName("commit_message")] string CommitMessage,
        [property: JsonPropertyName("commit_date")] string CommitDate,
        [property: JsonPropertyName("tag")] string Tag,
        [property: JsonPropertyName("commits")] string Commits,
        [property: JsonPropertyName("flags")] string[] Flags);
}

public sealed record BindsRequest : CommandRequest;

public sealed record VersionRequest : CommandRequest;

public sealed record CursorPosRequest : CommandRequest;

public sealed record ConfigErrorsRequest : CommandRequest;

public sealed record GetOptionRequest(string Name) : CommandRequest;

public sealed record SystemInfoRequest : CommandRequest;

public sealed record LockedRequest : CommandRequest;

public sealed record LayoutsRequest : CommandRequest;

public sealed record ClickKillRequest : CommandRequest;

public sealed record RollingLogRequest : CommandRequest;

public sealed record KeywordRequest(string Name, string Value) : CommandRequest;

public sealed record NotifyRequest : CommandRequest;

public sealed record DismissNotifyRequest : CommandRequest;

public sealed record GetPropRequest(string Selector, string Property) : CommandRequest;

public sealed record SetPropRequest(string Selector, string Property, string Value) : CommandRequest;

public sealed record BatchRequest(string Payload) : CommandRequest;
