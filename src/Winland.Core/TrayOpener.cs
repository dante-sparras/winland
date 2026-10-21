namespace Winland;

/// <summary>
/// The command that opens a file from the tray menu. The file's type supplies the command when Windows has one,
/// and Notepad opens it otherwise. The program is never elevated.
/// </summary>
public static class TrayOpener
{
    public static Launch Open(string path, string? association)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new Launch(Command(path, association), null);
    }

    /// <summary>
    /// <paramref name="association"/> is the open command for the file's type, with <c>%1</c>, <c>%L</c> or <c>%*</c>
    /// for the path, or null when the type has none.
    /// </summary>
    public static string Command(string path, string? association)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (string.IsNullOrWhiteSpace(association))
            return "notepad.exe " + Quote(path);

        var template = association.Trim();
        var filled = new System.Text.StringBuilder(template.Length + path.Length + 2);
        var replaced = false;
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == '%' && i + 1 < template.Length && template[i + 1] is '1' or '*' or 'L' or 'l')
            {
                var quoted = i > 0 && template[i - 1] == '"';
                filled.Append(quoted ? path : Quote(path));
                replaced = true;
                i++;
                continue;
            }

            filled.Append(template[i]);
        }

        if (!replaced)
            filled.Append(' ').Append(Quote(path));

        return filled.ToString();
    }

    private static string Quote(string path) => "\"" + path.Replace("\"", "", StringComparison.Ordinal) + "\"";
}
