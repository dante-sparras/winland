using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winland.Application;

/// <summary>The replies to the smaller requests: binds, version, the cursor, config errors, and how a batch splits.</summary>
public static partial class CommandProtocol
{
    public static IReadOnlyList<string> SplitBatch(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // As Winland splits a batch: at every ;, with \; for a ; and \\ for a \ in a command. A backslash
        // before anything else stays, and each command is trimmed, an empty one too.
        var commands = new List<string>();
        var command = new StringBuilder();
        for (var i = 0; i <= payload.Length; i++)
        {
            if (i == payload.Length || payload[i] == ';')
            {
                commands.Add(command.ToString().Trim());
                command.Clear();
                continue;
            }

            if (payload[i] == '\\' && i + 1 < payload.Length && payload[i + 1] is '\\' or ';')
                i++;

            command.Append(payload[i]);
        }

        return commands;
    }

    public static string FormatBinds(IReadOnlyList<Bind> binds)
    {
        ArgumentNullException.ThrowIfNull(binds);
        var views = new BindJson[binds.Count];
        for (var i = 0; i < binds.Count; i++)
            views[i] = BindOf(binds[i]);

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

    public static string FormatErrors(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return JsonSerializer.Serialize(errors, Relaxed);
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
        if (modifiers.HasFlag(Modifier.Caps))
            mask |= 1 << 1;
        if (modifiers.HasFlag(Modifier.Mod2))
            mask |= 1 << 4;
        if (modifiers.HasFlag(Modifier.Mod3))
            mask |= 1 << 5;
        if (modifiers.HasFlag(Modifier.Super))
            mask |= 1 << 6;
        if (modifiers.HasFlag(Modifier.Mod5))
            mask |= 1 << 7;

        return mask;
    }

    private static BindJson BindOf(Bind bind)
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
            bind.Flags.HasFlag(BindFlag.NonConsuming),
            bind.Description is not null,
            ModMask(bind.Modifiers),
            key,
            keycode,
            bind.Description ?? "",
            bind.Dispatcher,
            bind.Arguments);
    }

    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record BindJson(
        [property: JsonPropertyName("locked")] bool Locked,
        [property: JsonPropertyName("mouse")] bool Mouse,
        [property: JsonPropertyName("release")] bool Release,
        [property: JsonPropertyName("repeat")] bool Repeat,
        [property: JsonPropertyName("non_consuming")] bool NonConsuming,
        [property: JsonPropertyName("has_description")] bool HasDescription,
        [property: JsonPropertyName("modmask")] int Modmask,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("keycode")] int Keycode,
        [property: JsonPropertyName("description")] string Description,
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
