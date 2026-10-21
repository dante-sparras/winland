using System.Globalization;

namespace Winland.Domain;

/// <summary>
/// A window's content type, as Winland names them: 0 none, 1 photo, 2 video, 3 game. Windows reports no
/// content type, so a window has one only when a <c>content</c> window rule gives it.
/// </summary>
internal static class ContentTypes
{
    private static readonly string[] Names = ["none", "photo", "video", "game"];

    /// <summary>A number from 0 to 3, or a name. Anything else is none.</summary>
    public static int FromString(string text)
    {
        var trimmed = text.Trim();
        if (int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            return number is >= 0 and <= 3 ? number : 0;

        var index = Array.IndexOf(Names, trimmed);
        return index < 0 ? 0 : index;
    }

    public static string Name(int type) => type is >= 0 and <= 3 ? Names[type] : "";
}
