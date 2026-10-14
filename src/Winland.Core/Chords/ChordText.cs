using System.Collections.Concurrent;
using System.Globalization;

namespace Winland.Core.Chords;

/// <summary>Reads and writes a chord as <c>Super+Shift+S</c> or <c>F9</c>.</summary>
public static class ChordText
{
    // The book sorts by this text, the file is written from it, and the Menu shows it on every list. A chord's text never changes,
    // and there are a few hundred chords at most, so each is built once.
    private static readonly ConcurrentDictionary<Chord, string> Texts = new();

    /// <summary>The text form of <paramref name="chord"/>.</summary>
    public static string Format(Chord chord) => Texts.GetOrAdd(chord, static chord => Build(chord));

    private static string Build(Chord chord)
    {
        var parts = new List<string>(5);
        if (chord.Modifiers.HasFlag(ChordModifiers.Super))
        {
            parts.Add("Super");
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (chord.Modifiers.HasFlag(ChordModifiers.Alt))
        {
            parts.Add("Alt");
        }

        parts.Add(FormatKey(chord.Key));
        return string.Join('+', parts);
    }

    /// <summary>Parses <paramref name="text"/>. Unknown modifiers or keys return false.</summary>
    public static bool TryParse(string text, out Chord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = ChordModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!TryModifier(parts[i], out var flag) || modifiers.HasFlag(flag))
            {
                return false;
            }

            modifiers |= flag;
        }

        if (!TryKey(parts[^1], out var key))
        {
            return false;
        }

        chord = new Chord(modifiers, key);
        return true;
    }

    private static string FormatKey(ChordKey key) => key switch
    {
        >= ChordKey.D1 and <= ChordKey.D9 => ((int)key - (int)ChordKey.D1 + 1).ToString(CultureInfo.InvariantCulture),
        _ => key.ToString(),
    };

    private static bool TryModifier(string text, out ChordModifiers flag)
    {
        flag = text.ToUpperInvariant() switch
        {
            "SUPER" or "WIN" => ChordModifiers.Super,
            "SHIFT" => ChordModifiers.Shift,
            "CTRL" or "CONTROL" => ChordModifiers.Ctrl,
            "ALT" => ChordModifiers.Alt,
            _ => ChordModifiers.None,
        };
        return flag != ChordModifiers.None || text.Equals("None", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryKey(string text, out ChordKey key)
    {
        if (text.Length == 1 && text[0] is >= '1' and <= '9')
        {
            key = ChordKey.D1 + (text[0] - '1');
            return true;
        }

        if (text.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            key = ChordKey.Return;
            return true;
        }

        if (text.Equals("Esc", StringComparison.OrdinalIgnoreCase))
        {
            key = ChordKey.Escape;
            return true;
        }

        // Enum.TryParse reads a number as the key with that value, so "0" would bind another key and say nothing.
        // It reads a comma as several names joined into one value, so "W,Q" would bind T.
        if (text.Length == 0 || char.IsAsciiDigit(text[0]) || text[0] is '-' or '+' || text.Contains(','))
        {
            key = default;
            return false;
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && Enum.IsDefined(key);
    }
}
