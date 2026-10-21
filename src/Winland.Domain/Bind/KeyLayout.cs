namespace Winland.Domain;

/// <summary>Unshifted characters a keyboard layout produces, keyed by virtual key.</summary>
public sealed class KeyLayout
{
    public static KeyLayout UnitedStates { get; } = CreateUnitedStates();

    private readonly Dictionary<int, string> _characters;
    private readonly HashSet<int> _dead;
    private readonly Dictionary<int, string> _symbols;
    private readonly Dictionary<string, int> _bySymbol;

    private KeyLayout(Dictionary<int, string> characters, HashSet<int> dead, bool hasAltGr)
    {
        _characters = characters;
        _dead = dead;
        HasAltGr = hasAltGr;
        _symbols = new Dictionary<int, string>(characters.Count);
        _bySymbol = new Dictionary<string, int>(characters.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (virtualKey, character) in characters)
        {
            if (dead.Contains(virtualKey))
                continue;

            var symbol = KeyNames.Canonical(character);
            if (symbol is null || symbol.Length == 0)
                continue;

            _symbols[virtualKey] = symbol;
            _bySymbol[symbol] = virtualKey;
        }
    }

    public bool HasAltGr { get; }

    public static KeyLayout From(
        IReadOnlyDictionary<int, string>? characters,
        bool hasAltGr = false,
        IReadOnlyCollection<int>? deadKeys = null)
    {
        var copy = new Dictionary<int, string>();
        if (characters is not null)
        {
            foreach (var (virtualKey, character) in characters)
                copy[virtualKey] = character;
        }

        var dead = new HashSet<int>();
        if (deadKeys is not null)
        {
            foreach (var virtualKey in deadKeys)
                dead.Add(virtualKey);
        }

        return new KeyLayout(copy, dead, hasAltGr);
    }

    internal bool IsDead(int virtualKey) => _dead.Contains(virtualKey);

    internal bool TrySymbol(int virtualKey, out string symbol) =>
        _symbols.TryGetValue(virtualKey, out symbol!);

    public bool TryVirtualKey(string symbol, out int virtualKey) =>
        _bySymbol.TryGetValue(symbol, out virtualKey);

    internal IEnumerable<(string Character, string Symbol)> Symbols()
    {
        foreach (var (virtualKey, symbol) in _symbols)
            yield return (_characters[virtualKey], symbol);
    }

    private static KeyLayout CreateUnitedStates()
    {
        var characters = new Dictionary<int, string>(26 + 10 + 11);
        for (var index = 0; index < 26; index++)
            characters[0x41 + index] = ((char)('a' + index)).ToString();
        for (var index = 0; index < 10; index++)
            characters[0x30 + index] = ((char)('0' + index)).ToString();

        characters[0xBA] = ";";
        characters[0xBB] = "=";
        characters[0xBC] = ",";
        characters[0xBD] = "-";
        characters[0xBE] = ".";
        characters[0xBF] = "/";
        characters[0xC0] = "`";
        characters[0xDB] = "[";
        characters[0xDC] = "\\";
        characters[0xDD] = "]";
        characters[0xDE] = "'";
        return new KeyLayout(characters, [], false);
    }
}
