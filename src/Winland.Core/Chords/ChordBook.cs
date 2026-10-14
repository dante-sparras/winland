using System.Text.Json;

namespace Winland.Core.Chords;

/// <summary>
/// The chord map in <c>binds.json</c>. A missing file is written from the built-in map and the plugins' default chords.
/// A file with no <c>include</c> is the whole map. A file that includes others merges with them:
/// the included chords come first, and this file adds, replaces, or removes (<c>command</c> null) one chord.
/// </summary>
public sealed class ChordBook : IChordTable
{
    /// <summary>The file's name in the Winland config folder.</summary>
    public const string FileName = "binds.json";
    private readonly Dictionary<Chord, ShellCommand> _commands;
    private readonly Dictionary<Chord, ShellCommand.PluginCommand> _holds;

    private ChordBook(Dictionary<Chord, ShellCommand> commands, Dictionary<Chord, ShellCommand.PluginCommand> holds, IReadOnlyList<string> warnings)
    {
        _commands = commands;
        _holds = holds;
        Warnings = warnings;
        Bindings = commands.Select(pair => new ChordBinding(pair.Key, pair.Value, false))
            .Concat(holds.Select(pair => new ChordBinding(pair.Key, pair.Value, true)))
            .OrderBy(binding => ChordText.Format(binding.Chord), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Lines that were skipped while reading a file.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Every chord the book binds, held ones included, in the ordinal order of their chord text.</summary>
    public IReadOnlyList<ChordBinding> Bindings { get; }

    /// <summary>The built-in chords. No plugin binds anything.</summary>
    public static ChordBook Defaults() => Defaults([]);

    /// <summary>
    /// The built-in chords, then each plugin's <c>chords</c> in ordinal id order.
    /// A chord the built-in map or an earlier plugin already binds stays with it.
    /// </summary>
    /// <param name="plugins">Discovered manifests.</param>
    /// <returns>The map a missing <c>binds.json</c> is written from.</returns>
    public static ChordBook Defaults(IReadOnlyList<DeclaredPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        var commands = new Dictionary<Chord, ShellCommand>();
        foreach (var (chord, command) in ChordMap.Shared.Bindings)
        {
            commands[chord] = command;
        }

        var holds = new Dictionary<Chord, ShellCommand.PluginCommand>();
        var warnings = new List<string>();
        foreach (var manifest in plugins.OrderBy(plugin => plugin.Id, StringComparer.Ordinal))
        {
            foreach (var binding in manifest.Chords)
            {
                if (!ChordText.TryParse(binding.Chord, out var chord))
                {
                    continue;
                }

                var declared = Find(plugins, manifest.Id, binding.Command);
                if (declared is null)
                {
                    continue;
                }

                if (commands.ContainsKey(chord) || holds.ContainsKey(chord))
                {
                    warnings.Add(manifest.Id + " does not get " + binding.Chord + ", which is already bound");
                    continue;
                }

                var command = new ShellCommand.PluginCommand(manifest.Id, declared.Name, BindPhase.Run);
                if (declared.Hold)
                {
                    holds[chord] = command;
                }
                else
                {
                    commands[chord] = command;
                }
            }
        }

        return new ChordBook(commands, holds, warnings);
    }

    /// <summary>Reads <paramref name="path"/>. A missing file is created from <see cref="Defaults(IReadOnlyList{DeclaredPlugin})"/>.</summary>
    /// <param name="path">Full path of <c>binds.json</c>.</param>
    /// <param name="plugins">Discovered manifests. Null reads a <c>plugin:</c> line by its shape alone.</param>
    /// <returns>The map.</returns>
    public static ChordBook Load(string path, IReadOnlyList<DeclaredPlugin>? plugins = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            var seeded = Defaults(plugins ?? []);
            seeded.Save(path);
            return seeded;
        }

        return ReadExisting(path, plugins);
    }

    /// <summary>
    /// Reads <paramref name="path"/>. A missing file is <see cref="Defaults(IReadOnlyList{DeclaredPlugin})"/> and is not created.
    /// </summary>
    /// <param name="path">Full path of <c>binds.json</c>.</param>
    /// <param name="plugins">Discovered manifests. Null reads a <c>plugin:</c> line by its shape alone.</param>
    /// <returns>The file when it exists, otherwise the built-in map.</returns>
    public static ChordBook Read(string path, IReadOnlyList<DeclaredPlugin>? plugins = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return Defaults(plugins ?? []);
        }

        return ReadExisting(path, plugins);
    }

    private static ChordBook ReadExisting(string path, IReadOnlyList<DeclaredPlugin>? plugins)
    {
        try
        {
            var read = Winland.Core.ConfigJson.Read(path);
            return Parse(read.Json, plugins).With(read.Notes);
        }
        catch (JsonException)
        {
            return Fallback(plugins, "binds.json is not valid JSON");
        }
    }

    private ChordBook With(IReadOnlyList<string> notes)
    {
        if (notes.Count == 0)
        {
            return this;
        }

        var warnings = new List<string>(notes.Count + Warnings.Count);
        warnings.AddRange(notes);
        warnings.AddRange(Warnings);
        return new ChordBook(_commands, _holds, warnings);
    }

    /// <summary>
    /// Reads JSON. A document that is not an object falls back to the defaults and keeps the file untouched.
    /// With <paramref name="plugins"/>, a <c>panel:</c> line must name a plugin that declares a panel, a <c>plugin:</c> line must name a command a manifest declares,
    /// and a command the manifest marks <c>hold</c> is held.
    /// Only a plugin command can be held.
    /// </summary>
    /// <param name="json">The file text.</param>
    /// <param name="plugins">Discovered manifests. Null reads a <c>plugin:</c> line by its shape alone.</param>
    /// <returns>The map, with a warning for each skipped line.</returns>
    public static ChordBook Parse(string json, IReadOnlyList<DeclaredPlugin>? plugins = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryList(document.RootElement, out var chords))
            {
                return Fallback(plugins, "binds.json has no chords array");
            }

            var commands = new Dictionary<Chord, ShellCommand>();
            var holds = new Dictionary<Chord, ShellCommand.PluginCommand>();
            var warnings = new List<string>();
            foreach (var entry in chords.EnumerateArray())
            {
                if (!TryEntry(entry, out var chordText, out var commandText, out var hold))
                {
                    warnings.Add("skipped a chord entry that is missing chord or command");
                    continue;
                }

                if (!ChordText.TryParse(chordText, out var chord))
                {
                    warnings.Add("skipped unknown chord " + chordText);
                    continue;
                }

                if (WindowsReserve.Keeps(chord))
                {
                    warnings.Add("skipped " + chordText + ": " + WindowsReserve.Reason);
                    continue;
                }

                if (commands.ContainsKey(chord) || holds.ContainsKey(chord))
                {
                    warnings.Add("skipped duplicate chord " + chordText);
                    continue;
                }

                if (!ChordCommands.TryParse(commandText, out var command) || command is null)
                {
                    warnings.Add("skipped unknown command " + commandText);
                    continue;
                }

                if (command is ShellCommand.OpenPanel panel && plugins is not null && !HasPanel(plugins, panel.Id))
                {
                    warnings.Add("skipped " + commandText + ": no running plugin declares that panel");
                    continue;
                }

                if (command is ShellCommand.PluginCommand plugin && plugins is not null)
                {
                    var declared = Find(plugins, plugin.Plugin, plugin.Name);
                    if (declared is null)
                    {
                        warnings.Add("skipped " + commandText + ": no plugin declares it");
                        continue;
                    }

                    if (hold && !declared.Hold)
                    {
                        warnings.Add("skipped " + chordText + ": " + commandText + " is not held");
                        continue;
                    }

                    command = plugin with { Name = declared.Name };
                    hold = declared.Hold;
                }

                if (hold)
                {
                    if (command is not ShellCommand.PluginCommand held)
                    {
                        warnings.Add("skipped " + chordText + ": only a plugin command can be held");
                        continue;
                    }

                    holds[chord] = held;
                    continue;
                }

                commands[chord] = command;
            }

            return new ChordBook(commands, holds, warnings);
        }
        catch (JsonException)
        {
            return Fallback(plugins, "binds.json is not valid JSON");
        }
    }

    /// <summary>Writes this book to <paramref name="path"/>.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var entries = new List<ChordEntry>();
        foreach (var (chord, command) in _commands.OrderBy(pair => ChordText.Format(pair.Key), StringComparer.Ordinal))
        {
            if (ChordCommands.IdOf(command) is string id)
            {
                entries.Add(new ChordEntry(ChordText.Format(chord), id, false));
            }
        }

        foreach (var (chord, command) in _holds.OrderBy(pair => ChordText.Format(pair.Key), StringComparer.Ordinal))
        {
            entries.Add(new ChordEntry(ChordText.Format(chord), ChordCommands.IdOf(command)!, true));
        }

        var json = JsonSerializer.Serialize(new ChordFile(1, entries), ChordJson.Default.ChordFile);
        Winland.Core.AtomicFile.Write(path, json);
    }

    /// <inheritdoc />
    public ShellCommand? Resolve(Chord chord) => _commands.GetValueOrDefault(chord);

    /// <inheritdoc />
    public bool IsHold(Chord chord) => _holds.ContainsKey(chord);

    /// <inheritdoc />
    public ShellCommand? ResolveHold(Chord chord) => _holds.GetValueOrDefault(chord);

    // An escape that is half of a surrogate pair is valid JSON, and System.Text.Json throws InvalidOperationException
    // when it compares such a name or reads such a string. That is a file with no list, or a line that names nothing.
    private static bool TryList(JsonElement root, out JsonElement chords)
    {
        chords = default;
        try
        {
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("chords", out chords)
                && chords.ValueKind == JsonValueKind.Array;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryEntry(JsonElement entry, out string chord, out string command, out bool hold)
    {
        chord = "";
        command = "";
        hold = false;
        try
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("chord", out var chordProperty)
                || !entry.TryGetProperty("command", out var commandProperty)
                || chordProperty.ValueKind != JsonValueKind.String
                || commandProperty.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            chord = chordProperty.GetString() ?? "";
            command = commandProperty.GetString() ?? "";
            hold = entry.TryGetProperty("hold", out var holdProperty)
                && holdProperty.ValueKind == JsonValueKind.True;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static DeclaredCommand? Find(IReadOnlyList<DeclaredPlugin> plugins, string plugin, string name)
    {
        foreach (var manifest in plugins)
        {
            var answers = manifest.Id.Equals(plugin, StringComparison.Ordinal)
                || (manifest.ClonedFrom is { Length: > 0 } from && from.Equals(plugin, StringComparison.Ordinal));
            if (!answers)
            {
                continue;
            }

            foreach (var command in manifest.Commands)
            {
                if (command.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return command;
                }
            }
        }

        return null;
    }

    private static bool HasPanel(IReadOnlyList<DeclaredPlugin> plugins, string id)
    {
        foreach (var manifest in plugins)
        {
            var answers = manifest.Id.Equals(id, StringComparison.Ordinal)
                || (manifest.ClonedFrom is { Length: > 0 } from && from.Equals(id, StringComparison.Ordinal));
            if (answers && manifest.HasPanel)
            {
                return true;
            }
        }

        return false;
    }

    private static ChordBook Fallback(IReadOnlyList<DeclaredPlugin>? plugins, string warning)
    {
        var defaults = Defaults(plugins ?? []);
        return new ChordBook(defaults._commands, defaults._holds, [warning]);
    }

    internal sealed record ChordFile(int Version, List<ChordEntry> Chords);

    internal sealed record ChordEntry(string Chord, string Command, bool Hold);
}

/// <summary>One line of the book.</summary>
/// <param name="Chord">The keys.</param>
/// <param name="Command">What they run.</param>
/// <param name="Hold">True when the chord is held: the plugin hears the key go down and come up.</param>
public sealed record ChordBinding(Chord Chord, ShellCommand Command, bool Hold);
