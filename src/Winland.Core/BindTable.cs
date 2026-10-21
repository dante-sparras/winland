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
    private readonly bool _hasDeviceBind;

    private BindTable(Bind[] entries, int dragThreshold, int scrollDelay, int longPressDelay)
    {
        LongPressDelay = longPressDelay;
        _entries = Array.AsReadOnly(entries);
        DragThreshold = dragThreshold;
        ScrollDelay = scrollDelay;
        _submaps = new HashSet<string>(StringComparer.Ordinal);
        var universal = new List<Bind>();
        var hasDevice = false;
        foreach (var bind in entries)
        {
            if (bind.Devices is not null)
                hasDevice = true;
            if (bind.Submap is string name && name.Length > 0)
                _submaps.Add(name);
            if (bind.Flags.HasFlag(BindFlag.SubmapUniversal))
                universal.Add(bind);
        }

        _hasDeviceBind = hasDevice;

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

    /// <summary>True when any bind names a device. The hook reads this before it looks at a key.</summary>
    internal bool HasDeviceBind => _hasDeviceBind;

    internal bool HasSubmap(string name) => name.Length > 0 && _submaps.Contains(name);

    internal IReadOnlyList<Bind> Active(string submap) =>
        _active.TryGetValue(submap, out var found) ? found : _universal.Length == 0 ? NoBinds : _universal;

    internal int DragThreshold { get; }

    internal int ScrollDelay { get; }

    /// <summary>How long a key is held before a long-press bind runs, in milliseconds. It does not follow a Windows setting.</summary>
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

        var options = BindOptions.From(config);
        return new BindTable([.. binds], options.DragThreshold, options.ScrollEventDelay, LongPressMilliseconds);
    }

    /// <summary>How long a key is held before a long-press bind runs, in milliseconds.</summary>
    private const int LongPressMilliseconds = 600;
}
