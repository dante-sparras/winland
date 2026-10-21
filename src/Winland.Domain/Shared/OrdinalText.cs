namespace Winland.Domain;

/// <summary>Compares config words, dispatcher names and layout messages, which ignore case.</summary>
internal static class OrdinalText
{
    public static bool Is(string text, string expected) =>
        text.Equals(expected, StringComparison.OrdinalIgnoreCase);
}
