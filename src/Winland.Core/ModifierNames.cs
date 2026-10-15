using static Winland.OrdinalText;

namespace Winland;

/// <summary>Reads a modifier list such as <c>SUPER SHIFT</c>, as a bind and <c>sendshortcut</c> write it.</summary>
internal static class ModifierNames
{
    internal static bool TryParse(string text, out Modifier modifiers, out string error)
    {
        modifiers = Modifier.None;
        error = "";
        if (text.Length == 0)
            return true;

        foreach (var token in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseOne(token, out var one))
            {
                error = $"Invalid modifier '{token}'.";
                return false;
            }

            modifiers |= one;
        }

        return true;
    }

    private static bool TryParseOne(string token, out Modifier modifier)
    {
        if (Is(token, "SUPER") || Is(token, "WIN") || Is(token, "LOGO") || Is(token, "MOD4"))
        {
            modifier = Modifier.Super;
            return true;
        }

        if (Is(token, "ALT") || Is(token, "MOD1"))
        {
            modifier = Modifier.Alt;
            return true;
        }

        if (Is(token, "CTRL") || Is(token, "CONTROL"))
        {
            modifier = Modifier.Ctrl;
            return true;
        }

        if (Is(token, "SHIFT"))
        {
            modifier = Modifier.Shift;
            return true;
        }

        modifier = Modifier.None;
        return false;
    }
}
