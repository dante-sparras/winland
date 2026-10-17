using System.Globalization;
using static Winland.OrdinalText;

namespace Winland;

/// <summary>One consistent bind table. Published as a whole on config load and reload.</summary>
public sealed class BindTable
{
    public static BindTable Empty { get; } = new([], dragThreshold: 0, scrollDelay: 300, longPressDelay: 600);

    private static readonly Bind[] NoBinds = [];

    private readonly IReadOnlyList<Bind> _entries;
    private readonly Dictionary<string, Bind[]> _active;
    private readonly HashSet<string> _submaps;
    private readonly Bind[] _universal;

    private BindTable(Bind[] entries, int dragThreshold, int scrollDelay, int longPressDelay)
    {
        LongPressDelay = longPressDelay;
        _entries = Array.AsReadOnly(entries);
        DragThreshold = dragThreshold;
        ScrollDelay = scrollDelay;
        _submaps = new HashSet<string>(StringComparer.Ordinal);
        var universal = new List<Bind>();
        foreach (var bind in entries)
        {
            if (bind.Submap is string name && name.Length > 0)
                _submaps.Add(name);
            if (bind.Flags.HasFlag(BindFlag.SubmapUniversal))
                universal.Add(bind);
        }

        _universal = universal.ToArray();
        var names = new HashSet<string>(_submaps, StringComparer.Ordinal) { "" };
        var active = new Dictionary<string, Bind[]>(names.Count, StringComparer.Ordinal);
        foreach (var name in names)
        {
            var chosen = new List<Bind>(entries.Length);
            foreach (var bind in entries)
            {
                if (bind.Flags.HasFlag(BindFlag.SubmapUniversal) || (bind.Submap ?? "") == name)
                    chosen.Add(bind);
            }

            active.Add(name, chosen.ToArray());
        }

        _active = active;
    }

    public IReadOnlyList<Bind> Entries => _entries;

    internal bool HasSubmap(string name) => name.Length > 0 && _submaps.Contains(name);

    internal IReadOnlyList<Bind> Active(string submap) =>
        _active.TryGetValue(submap, out var found) ? found : _universal.Length == 0 ? NoBinds : _universal;

    internal int DragThreshold { get; }

    internal int ScrollDelay { get; }

    /// <summary>How long a key is held before a long-press bind runs: <c>input:repeat_delay</c>, as in the pinned release.</summary>
    internal int LongPressDelay { get; }

    internal static BindTable From(ConfigDocument config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var binds = new List<Bind>();
        foreach (var directive in config.Binds)
        {
            switch (directive)
            {
                case Bind bind:
                    binds.Add(bind);
                    break;
                case Unbind unbind:
                    binds.RemoveAll(bind => bind.Modifiers == unbind.Modifiers && Is(bind.Key, unbind.Key));
                    break;
                case UnbindAll:
                    binds.Clear();
                    break;
            }
        }

        return new BindTable([.. binds], ReadThreshold(config), ReadScrollDelay(config), ReadRepeatDelay(config));
    }

    private static int ReadThreshold(ConfigDocument config)
    {
        var threshold = 0;
        foreach (var setting in config.Settings)
        {
            if (setting.Keyword == "binds:drag_threshold" && int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                threshold = Math.Max(0, parsed);
        }

        return threshold;
    }

    private static int ReadScrollDelay(ConfigDocument config)
    {
        var delay = 300;
        foreach (var setting in config.Settings)
        {
            if (setting.Keyword == "binds:scroll_event_delay" && int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                delay = Math.Clamp(parsed, 0, 2000);
        }

        return delay;
    }

    private static int ReadRepeatDelay(ConfigDocument config)
    {
        var delay = 600;
        foreach (var setting in config.Settings)
        {
            if (setting.Keyword == "input:repeat_delay" && int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 0 and <= 2000)
                delay = parsed;
        }

        return delay;
    }
}
