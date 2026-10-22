namespace Winland.Domain;

/// <summary>The Options one record of options declares, and the record a config's lines make of them.</summary>
internal sealed class OptionSet<TOptions>
    where TOptions : class
{
    private readonly Dictionary<string, Option<TOptions>> _byName;

    public OptionSet(IEnumerable<Option<TOptions>> options) =>
        _byName = options.ToDictionary(option => option.Name, StringComparer.Ordinal);

    public IEnumerable<IOption> All => _byName.Values;

    /// <summary><paramref name="options"/> with every line of <paramref name="document"/> that sets one of these Options, in order.</summary>
    public TOptions From(TOptions options, ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var setting in document.Settings)
        {
            if (_byName.TryGetValue(setting.Keyword, out var option))
                options = option.Apply(options, setting.Value) ?? options;
        }

        return options;
    }

    /// <summary><paramref name="options"/> with one Option set, or null when there is no such Option or the value does not read.</summary>
    public TOptions? Apply(TOptions options, string name, string text) =>
        _byName.TryGetValue(name, out var option) ? option.Apply(options, text) : null;

    public bool TryReply(string name, TOptions applied, out string reply)
    {
        reply = "";
        if (!_byName.TryGetValue(name, out var option))
            return false;

        reply = option.Reply(applied);
        return true;
    }
}
