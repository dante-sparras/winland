namespace Winland.Core.Chords;

/// <summary>
/// A chord table that can be replaced while it is in use, so a saved <c>bindings.json</c> takes effect without a restart (ADR 0067).
/// Whoever holds this sees the newest table on the next key. Safe from any thread.
/// </summary>
public sealed class ChordSwitch : IChordTable
{
    private IChordTable _current;

    /// <summary>Starts with <paramref name="chords"/>.</summary>
    /// <param name="chords">The table in use until <see cref="Use"/> replaces it.</param>
    public ChordSwitch(IChordTable chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        _current = chords;
    }

    /// <summary>The table in use now.</summary>
    public IChordTable Current => Volatile.Read(ref _current);

    /// <summary>Replaces the table. A key already down finishes against whichever table its next edge finds.</summary>
    /// <param name="chords">The table to use from now on.</param>
    public void Use(IChordTable chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        Volatile.Write(ref _current, chords);
    }

    /// <inheritdoc />
    public ShellCommand? Resolve(Chord chord) => Current.Resolve(chord);

    /// <inheritdoc />
    public bool IsHold(Chord chord) => Current.IsHold(chord);

    /// <inheritdoc />
    public ShellCommand? ResolveHold(Chord chord) => Current.ResolveHold(chord);

    /// <summary>True when <paramref name="text"/> is a chord file worth loading: a JSON object with a <c>chords</c> list. A file caught mid-save is not.</summary>
    /// <param name="text">The file's text.</param>
    /// <returns>False for an empty or unfinished file, and for one with no <c>chords</c> list.</returns>
    public static bool Parses(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            // A file with no chords list is read as the built-in map, so an empty object or a misspelt key would throw the person's keys away.
            using var document = System.Text.Json.JsonDocument.Parse(text);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("chords", out var chords)
                && chords.ValueKind == System.Text.Json.JsonValueKind.Array;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            // InvalidOperationException is a name with half of a surrogate pair: valid JSON that System.Text.Json cannot compare.
            return false;
        }
    }
}
