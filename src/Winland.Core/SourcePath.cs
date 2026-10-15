using System.Text;

namespace Winland;

public static class SourcePath
{
    public static string Expand(string path)
    {
        return ExpandTilde(ExpandEnvironment(path));
    }

    public static bool HasGlob(string path) =>
        path.Contains('*') || path.Contains('?') || path.Contains('[');

    public static bool Matches(string pattern, string path) =>
        Match(pattern.Replace('\\', '/'), path.Replace('\\', '/'));

    private static string ExpandEnvironment(string path)
    {
        var builder = new StringBuilder(path.Length);
        for (var i = 0; i < path.Length; i++)
        {
            if (path[i] != '$' || i + 1 >= path.Length)
            {
                builder.Append(path[i]);
                continue;
            }

            var token = EnvironmentToken(path, i);
            if (token.Length == 0)
            {
                builder.Append('$');
                continue;
            }

            var name = token[1] == '{' ? token[2..^1] : token[1..];
            var value = Environment.GetEnvironmentVariable(name);
            if (value is null)
                builder.Append(token);
            else
                builder.Append(value);

            i += token.Length - 1;
        }

        return builder.ToString();
    }

    private static string EnvironmentToken(string path, int start)
    {
        if (path[start + 1] == '{')
        {
            var end = path.IndexOf('}', start + 2);
            if (end < 0 || end == start + 2)
                return "";

            return path[start..(end + 1)];
        }

        var index = start + 1;
        if (index >= path.Length || (!char.IsAsciiLetter(path[index]) && path[index] != '_'))
            return "";

        index++;
        while (index < path.Length && (char.IsAsciiLetterOrDigit(path[index]) || path[index] == '_'))
            index++;

        return path[start..index];
    }

    private static string ExpandTilde(string path)
    {
        if (path.Length == 0 || path[0] != '~')
            return path;

        if (path.Length > 1 && path[1] is not ('/' or '\\'))
            return path;

        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return path.Length == 1 ? home : home + path[1..];
    }

    private static bool Match(string pattern, string text)
    {
        var patternIndex = 0;
        var textIndex = 0;
        while (patternIndex < pattern.Length)
        {
            if (pattern[patternIndex] == '*')
            {
                patternIndex++;
                while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
                    patternIndex++;

                if (patternIndex == pattern.Length)
                    return !text[textIndex..].Contains('/');

                for (var skip = 0; textIndex + skip <= text.Length; skip++)
                {
                    if (skip > 0 && text[textIndex + skip - 1] == '/')
                        return false;

                    if (Match(pattern[patternIndex..], text[(textIndex + skip)..]))
                        return true;

                    if (textIndex + skip == text.Length)
                        return false;
                }

                return false;
            }

            if (textIndex >= text.Length)
                return false;

            if (pattern[patternIndex] == '?')
            {
                if (text[textIndex] == '/')
                    return false;

                patternIndex++;
                textIndex++;
                continue;
            }

            if (pattern[patternIndex] == '[')
            {
                if (!TryClass(pattern, ref patternIndex, text[textIndex]))
                    return false;

                textIndex++;
                continue;
            }

            if (!CharsEqual(pattern[patternIndex], text[textIndex]))
                return false;

            patternIndex++;
            textIndex++;
        }

        return textIndex == text.Length;
    }

    private static bool TryClass(string pattern, ref int index, char candidate)
    {
        var cursor = index + 1;
        if (cursor >= pattern.Length)
            return false;

        var negated = pattern[cursor] == '!';
        if (negated)
            cursor++;

        var matched = false;
        var closed = false;
        while (cursor < pattern.Length)
        {
            if (pattern[cursor] == ']')
            {
                closed = true;
                cursor++;
                break;
            }

            var start = pattern[cursor];
            cursor++;
            if (cursor + 1 < pattern.Length && pattern[cursor] == '-' && pattern[cursor + 1] != ']')
            {
                var end = pattern[cursor + 1];
                cursor += 2;
                if (InRange(candidate, start, end))
                    matched = true;
            }
            else if (CharsEqual(start, candidate))
            {
                matched = true;
            }
        }

        if (!closed)
            return false;

        index = cursor;
        return negated ? !matched : matched;
    }

    private static bool InRange(char candidate, char start, char end)
    {
        var value = char.ToUpperInvariant(candidate);
        var lower = char.ToUpperInvariant(start);
        var upper = char.ToUpperInvariant(end);
        return value >= lower && value <= upper;
    }

    private static bool CharsEqual(char left, char right) =>
        char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
