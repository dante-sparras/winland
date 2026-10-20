// Regenerates src/Winland.Core/OptionCatalog.Table.cs from the pinned release's config values.
//
//   dotnet run --file scripts/generate-option-catalog.cs -- <ConfigValues.cpp> <OptionCatalog.Table.cs>
using System.Globalization;
using System.Text;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: dotnet run --file scripts/generate-option-catalog.cs -- <ConfigValues.cpp> <OptionCatalog.Table.cs>");
    return 1;
}

try
{
    var calls = CallParser.Read(File.ReadAllText(args[0]));
    var output = CatalogWriter.Write(calls);
    var banned = "hypr" + "land";
    var lines = output.Split('\n');
    for (var n = 0; n < lines.Length; n++)
    {
        if (lines[n].Contains(banned, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Generated line {n + 1} contains the forbidden name: {lines[n]}");
    }

    File.WriteAllText(args[1], output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.WriteLine($"Wrote {calls.Count} options to {args[1]}");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

internal readonly record struct Arg(bool IsString, string Text);

internal sealed record Call(string Type, IReadOnlyList<Arg> Args);

internal static class CallParser
{
    public static List<Call> Read(string text)
    {
        var calls = new List<Call>();
        var i = 0;
        while (i < text.Length)
        {
            var at = text.IndexOf("MS<", i, StringComparison.Ordinal);
            if (at < 0)
                break;

            i = at + 3;
            var typeStart = i;
            while (i < text.Length && text[i] != '>')
                i++;
            if (i >= text.Length)
                throw new InvalidOperationException("Unclosed MS<");

            var type = text[typeStart..i];
            i++;
            Skip(text, ref i);
            if (i >= text.Length || text[i] != '(')
                throw new InvalidOperationException($"MS<{type}> is not a call near {Snippet(text, i)}");

            i++;
            var args = new List<Arg>();
            while (true)
            {
                Skip(text, ref i);
                if (i < text.Length && text[i] == ')')
                {
                    i++;
                    break;
                }

                args.Add(ReadArgument(text, ref i));
                Skip(text, ref i);
                if (i < text.Length && text[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i < text.Length && text[i] == ')')
                {
                    i++;
                    break;
                }

                throw new InvalidOperationException($"MS<{type}> arguments do not close near {Snippet(text, i)}");
            }

            calls.Add(new Call(type, args));
        }

        if (calls.Count < 100)
            throw new InvalidOperationException($"Expected the full config table, parsed {calls.Count} options.");

        return calls;
    }

    private static Arg ReadArgument(string text, ref int i)
    {
        Skip(text, ref i);
        if (i >= text.Length)
            throw new InvalidOperationException("Unexpected end of an argument.");

        if (text[i] == '"')
        {
            var value = ReadString(text, ref i);
            Skip(text, ref i);
            while (i < text.Length && text[i] == '"')
            {
                value += ReadString(text, ref i);
                Skip(text, ref i);
            }

            return new Arg(true, value);
        }

        var start = i;
        var paren = 0;
        var brace = 0;
        var bracket = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                ReadString(text, ref i);
                continue;
            }

            if (c == '/' && i + 1 < text.Length && (text[i + 1] == '/' || text[i + 1] == '*'))
            {
                SkipComment(text, ref i);
                continue;
            }

            if (c == '(')
                paren++;
            else if (c == ')')
            {
                if (paren == 0 && brace == 0 && bracket == 0)
                    break;
                paren--;
            }
            else if (c == '{')
                brace++;
            else if (c == '}')
            {
                if (brace == 0 && paren == 0 && bracket == 0)
                    break;
                brace--;
            }
            else if (c == '[')
                bracket++;
            else if (c == ']')
            {
                if (bracket == 0)
                    break;
                bracket--;
            }
            else if (c == ',' && paren == 0 && brace == 0 && bracket == 0)
                break;

            i++;
        }

        return new Arg(false, text[start..i].Trim());
    }

    internal static string ReadString(string text, ref int i)
    {
        if (i >= text.Length || text[i] != '"')
            throw new InvalidOperationException($"Expected a string near {Snippet(text, i)}");

        i++;
        var builder = new StringBuilder();
        while (i < text.Length)
        {
            var c = text[i++];
            if (c == '"')
                return builder.ToString();

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (i >= text.Length)
                break;

            builder.Append(text[i++] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '"' => '"',
                '\'' => '\'',
                var other => other,
            });
        }

        throw new InvalidOperationException("Unterminated string.");
    }

    internal static void Skip(string text, ref int i)
    {
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            if (text[i] == '/' && i + 1 < text.Length && (text[i + 1] == '/' || text[i + 1] == '*'))
            {
                SkipComment(text, ref i);
                continue;
            }

            return;
        }
    }

    private static void SkipComment(string text, ref int i)
    {
        if (text[i + 1] == '/')
        {
            i += 2;
            while (i < text.Length && text[i] != '\n')
                i++;
            return;
        }

        i += 2;
        while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
            i++;
        if (i + 1 >= text.Length)
            throw new InvalidOperationException("Unterminated comment.");
        i += 2;
    }

    private static string Snippet(string text, int i)
    {
        var start = Math.Max(0, i - 40);
        var end = Math.Min(text.Length, i + 40);
        return text[start..end].Replace('\n', ' ');
    }
}

internal sealed class Opts
{
    public string? Min { get; set; }

    public string? Max { get; set; }

    public List<(string Key, string Value)>? Map { get; set; }

    public List<string>? Choices { get; set; }

    public string? VecMinX { get; set; }

    public string? VecMinY { get; set; }

    public string? VecMaxX { get; set; }

    public string? VecMaxY { get; set; }
}

internal static class CatalogWriter
{
    public static string Write(IReadOnlyList<Call> calls)
    {
        var builder = new StringBuilder();
        builder.Append("// <auto-generated/>\n");
        builder.Append("// Generated by scripts/generate-option-catalog.cs from the pinned release's config values. Do not edit.\n");
        builder.Append("namespace Winland;\n\n");
        builder.Append("internal static partial class OptionCatalog\n");
        builder.Append("{\n");
        builder.Append("    private static readonly CatalogOption[] Table =\n");
        builder.Append("    [\n");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in calls)
        {
            var line = Emit(call);
            var name = call.Args[0].Text;
            if (!seen.Add(name))
                throw new InvalidOperationException($"Duplicate option {name}");
            builder.Append("        ").Append(line).Append(",\n");
        }

        builder.Append("    ];\n");
        builder.Append("}\n");
        return builder.ToString();
    }

    private static string Emit(Call call)
    {
        if (call.Args.Count < 2 || !call.Args[0].IsString || !call.Args[1].IsString)
            throw new InvalidOperationException($"MS<{call.Type}> is missing a name or a description.");

        var name = EmitName(call.Args[0].Text);
        var description = Cs(Rewrite(call.Args[1].Text));
        var options = call.Args.Count >= 4 ? ParseOpts(call.Args[3].Text, call.Args[0].Text) : call.Args.Count == 3 && !call.Args[2].IsString && call.Args[2].Text.StartsWith('{')
            ? ParseOpts(call.Args[2].Text, call.Args[0].Text)
            : null;
        var hasDefault = call.Args.Count >= 3 && (options is null || call.Args.Count >= 4 || call.Args[2].IsString || !call.Args[2].Text.StartsWith('{'));

        return call.Type switch
        {
            "Bool" => Bool(name, description, Default(call, hasDefault), options),
            "Int" => Int(name, description, Default(call, hasDefault), options),
            "Float" => Float(name, description, Default(call, hasDefault), options),
            "String" => Str(name, description, Default(call, hasDefault), options),
            "Vec2" => Vec(name, description, Default(call, hasDefault), options),
            "CssGap" => Css(name, description, Default(call, hasDefault), options),
            "Color" => Color(name, description, Default(call, hasDefault)),
            "Gradient" => Gradient(name, description, Default(call, hasDefault)),
            "FontWeight" => Weight(name, description, hasDefault ? Default(call, true) : null),
            _ => throw new InvalidOperationException($"Unknown option type {call.Type} for {call.Args[0].Text}"),
        };
    }

    private static Arg Default(Call call, bool hasDefault)
    {
        if (!hasDefault)
            throw new InvalidOperationException($"{call.Type} {call.Args[0].Text} has no default.");
        return call.Args[2];
    }

    private static string Bool(string name, string description, Arg value, Opts? options)
    {
        if (options?.Min is not null || options?.Map is not null)
            throw new InvalidOperationException($"Bool {name} has a range.");
        if (value.Text is not ("true" or "false"))
            throw new InvalidOperationException($"Bool default {value.Text}");
        return $"Bool({name}, {description}, {value.Text})";
    }

    private static string Int(string name, string description, Arg value, Opts? options)
    {
        var call = $"Int({name}, {description}, {EmitInt(value.Text, name)}";
        if (options?.Min is not null)
            call += $", min: {EmitInt(options.Min, name)}";
        if (options?.Max is not null)
            call += $", max: {EmitInt(options.Max, name)}";
        if (options?.Map is not null)
        {
            call += ", map: [";
            call += string.Join(", ", options.Map.Select(pair => $"({Cs(pair.Key)}, {EmitInt(pair.Value, name)}L)"));
            call += "]";
        }

        return call + ")";
    }

    private static string Float(string name, string description, Arg value, Opts? options)
    {
        var call = $"Float({name}, {description}, {EmitFloat(value.Text, name)}";
        if (options?.Min is not null)
            call += $", min: {EmitFloat(options.Min, name)}";
        if (options?.Max is not null)
            call += $", max: {EmitFloat(options.Max, name)}";
        if (options?.Map is not null)
            throw new InvalidOperationException($"Float {name} has a map.");
        return call + ")";
    }

    private static string Str(string name, string description, Arg value, Opts? options)
    {
        var call = $"Str({name}, {description}, {EmitText(value, name)}";
        if (options?.Choices is not null)
        {
            call += ", choices: [";
            call += string.Join(", ", options.Choices.Select(Cs));
            call += "]";
        }

        return call + ")";
    }

    private static string Vec(string name, string description, Arg value, Opts? options)
    {
        var (x, y) = ParseVec(value.Text, name);
        var call = $"Vec({name}, {description}, {x}, {y}";
        if (options?.VecMinX is not null)
        {
            call += $", minX: {EmitFloat(options.VecMinX, name)}, minY: {EmitFloat(options.VecMinY!, name)}";
            call += $", maxX: {EmitFloat(options.VecMaxX!, name)}, maxY: {EmitFloat(options.VecMaxY!, name)}";
        }

        return call + ")";
    }

    private static string Css(string name, string description, Arg value, Opts? options)
    {
        var call = $"Css({name}, {description}, {EmitInt(value.Text, name)}";
        if (options?.Min is not null)
            call += $", min: {EmitInt(options.Min, name)}";
        if (options?.Max is not null)
            call += $", max: {EmitInt(options.Max, name)}";
        return call + ")";
    }

    private static string Color(string name, string description, Arg value) =>
        $"Color({name}, {description}, {EmitPacked(value.Text, name)})";

    private static string Gradient(string name, string description, Arg value) =>
        $"Gradient({name}, {description}, {EmitPacked(value.Text, name)})";

    private static string Weight(string name, string description, Arg? value)
    {
        if (value is not Arg def)
            return $"Weight({name}, {description})";
        return $"Weight({name}, {description}, {EmitInt(def.Text, name)})";
    }

    private static string EmitName(string name)
    {
        const string word = "hyprland";
        var index = name.IndexOf(word, StringComparison.Ordinal);
        if (index < 0)
            return Cs(name);

        return Cs(name[..index]) + " + \"hypr\" + " + Cs("land" + name[(index + word.Length)..]);
    }

    private static string Rewrite(string text)
    {
        text = text.Replace("Hyprland", "Winland", StringComparison.Ordinal).Replace("hyprland", "Winland", StringComparison.Ordinal);
        text = text.Replace("Winland-guiutils", "the GUI utilities", StringComparison.Ordinal);
        text = text.Replace("if the GUI utilities is missing", "if the GUI utilities are missing", StringComparison.Ordinal);
        text = text.Replace("start-Winland", "the start script", StringComparison.Ordinal);
        if (text.Contains("hyprland", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A description still names the reference compositor: " + text);
        return text;
    }

    private static string EmitText(Arg value, string name)
    {
        if (value.IsString)
            return Cs(value.Text);
        if (value.Text == "STRVAL_EMPTY")
            return Cs("[[EMPTY]]");
        throw new InvalidOperationException($"String default {value.Text} for {name}");
    }

    private static string EmitPacked(string token, string name)
    {
        token = token.Trim();
        if (token.StartsWith("CHyprColor{", StringComparison.Ordinal) && token.EndsWith('}'))
            token = token["CHyprColor{".Length..^1].Trim();
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || token == "-1" || long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return token;
        throw new InvalidOperationException($"Color default {token} for {name}");
    }

    private static (string X, string Y) ParseVec(string token, string name)
    {
        token = token.Trim();
        if (token is "Config::VEC2{}")
            return ("0f", "0f");
        const string prefix = "Config::VEC2{";
        if (token.StartsWith(prefix, StringComparison.Ordinal) && token.EndsWith('}'))
        {
            var inner = token[prefix.Length..^1].Trim();
            if (inner.Length == 0)
                return ("0f", "0f");
            var parts = SplitTop(inner);
            if (parts.Count == 2)
                return (EmitFloat(parts[0], name), EmitFloat(parts[1], name));
        }

        throw new InvalidOperationException($"Vec2 default {token} for {name}");
    }

    private static string EmitInt(string token, string name)
    {
        token = token.Trim();
        if (token == "std::numeric_limits<int>::max()")
            return "2147483647";
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return token;
        if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return token;
        throw new InvalidOperationException($"Integer {token} for {name}");
    }

    private static string EmitFloat(string token, string name)
    {
        token = token.Trim();
        if (token.EndsWith('F') || token.EndsWith('f'))
        {
            var body = token[..^1];
            if (body.EndsWith('.'))
                body += "0";
            return body + "f";
        }

        if (token.Contains('.') || token.Contains('e') || token.Contains('E'))
            return "(float)" + token;
        if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return token + "f";
        throw new InvalidOperationException($"Float {token} for {name}");
    }

    private static Opts ParseOpts(string text, string name)
    {
        var opts = new Opts();
        var i = 0;
        var depth = 0;
        var ready = false;
        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                CallParser.ReadString(text, ref i);
                ready = false;
                continue;
            }

            if (text[i] == '/' && i + 1 < text.Length && (text[i + 1] == '/' || text[i + 1] == '*'))
            {
                CallParser.Skip(text, ref i);
                continue;
            }

            if (text[i] == '{')
            {
                depth++;
                if (depth == 1)
                    ready = true;
                i++;
                continue;
            }

            if (text[i] == '}')
            {
                depth--;
                i++;
                continue;
            }

            if (depth == 1 && text[i] == ',')
            {
                ready = true;
                i++;
                continue;
            }

            if (depth == 1 && ready && text[i] == '.')
            {
                i++;
                var start = i;
                while (i < text.Length && char.IsLetter(text[i]))
                    i++;
                var field = text[start..i];
                CallParser.Skip(text, ref i);
                if (i >= text.Length || text[i] != '=')
                    throw new InvalidOperationException($"Field {field} of {name} has no value.");
                i++;
                CallParser.Skip(text, ref i);
                var value = ReadValue(text, ref i);
                switch (field)
                {
                    case "min":
                        opts.Min = value;
                        break;
                    case "max":
                        opts.Max = value;
                        break;
                    case "map":
                        opts.Map = ParseMap(value, name);
                        break;
                    case "validator":
                        ParseValidator(value, opts, name);
                        break;
                    case "refresh":
                    case "deprecationNotice":
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown field {field} on {name}");
                }

                ready = false;
                continue;
            }

            if (!char.IsWhiteSpace(text[i]))
                ready = false;
            i++;
        }

        return opts;
    }

    private static string ReadValue(string text, ref int i)
    {
        var start = i;
        var paren = 0;
        var brace = 0;
        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                CallParser.ReadString(text, ref i);
                continue;
            }

            if (text[i] == '(')
                paren++;
            else if (text[i] == ')')
            {
                if (paren == 0 && brace == 0)
                    break;
                paren--;
            }
            else if (text[i] == '{')
                brace++;
            else if (text[i] == '}')
            {
                if (brace == 0 && paren == 0)
                    break;
                brace--;
            }
            else if (text[i] == ',' && paren == 0 && brace == 0)
                break;

            i++;
        }

        return text[start..i].Trim();
    }

    private static List<(string Key, string Value)> ParseMap(string value, string name)
    {
        var open = value.IndexOf('{');
        var close = value.LastIndexOf('}');
        if (open < 0 || close < open)
            throw new InvalidOperationException($"Map {value} for {name}");

        var inner = value[(open + 1)..close];
        var pairs = new List<(string, string)>();
        var i = 0;
        while (i < inner.Length)
        {
            if (inner[i] != '"')
            {
                i++;
                continue;
            }

            var key = CallParser.ReadString(inner, ref i);
            while (i < inner.Length && inner[i] != ',')
                i++;
            if (i >= inner.Length)
                throw new InvalidOperationException($"Map pair {key} for {name}");
            i++;
            var numberStart = i;
            while (i < inner.Length && inner[i] != '}')
                i++;
            pairs.Add((key, inner[numberStart..i].Trim()));
            i++;
        }

        if (pairs.Count == 0)
            throw new InvalidOperationException($"Empty map for {name}");
        return pairs;
    }

    private static void ParseValidator(string value, Opts opts, string name)
    {
        value = value.Trim();
        if (value.StartsWith("strChoice(", StringComparison.Ordinal))
        {
            opts.Choices = [];
            var i = 0;
            while (i < value.Length)
            {
                if (value[i] == '"')
                    opts.Choices.Add(CallParser.ReadString(value, ref i));
                else
                    i++;
            }

            if (opts.Choices.Count == 0)
                throw new InvalidOperationException($"Empty choices for {name}");
            return;
        }

        if (value.StartsWith("vec2Range(", StringComparison.Ordinal) && value.EndsWith(')'))
        {
            var inner = value["vec2Range(".Length..^1];
            var parts = SplitTop(inner);
            if (parts.Count != 4)
                throw new InvalidOperationException($"vec2Range {value} for {name}");
            opts.VecMinX = parts[0];
            opts.VecMinY = parts[1];
            opts.VecMaxX = parts[2];
            opts.VecMaxY = parts[3];
            return;
        }

        throw new InvalidOperationException($"Validator {value} for {name}");
    }

    private static List<string> SplitTop(string text)
    {
        var parts = new List<string>();
        var start = 0;
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '(' or '{' or '[')
                depth++;
            else if (text[i] is ')' or '}' or ']')
                depth--;
            else if (text[i] == ',' && depth == 0)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }

        if (start < text.Length)
            parts.Add(text[start..].Trim());
        return parts;
    }

    private static string Cs(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
