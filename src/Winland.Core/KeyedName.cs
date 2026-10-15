namespace Winland;

internal static class KeyedName
{
    public static bool TrySplit(string name, out string category, out string key)
    {
        category = "";
        key = "";
        var open = name.IndexOf('[');
        if (open <= 0 || name[^1] != ']' || name.IndexOf('[', open + 1) >= 0)
            return false;

        if (name.IndexOf(']', open + 1) != name.Length - 1)
            return false;

        category = name[..open];
        key = name[(open + 1)..^1];
        return !category.Contains(':');
    }

    public static bool TryInline(string lhs, out string keyword)
    {
        keyword = "";
        var open = lhs.IndexOf('[');
        var marker = lhs.IndexOf("]:", StringComparison.Ordinal);
        if (open <= 0 || marker < open + 1)
            return false;

        var category = lhs[..open];
        var name = lhs[(marker + 2)..];
        if (category.Length == 0 || category.Contains(':') || category.Contains(']') || name.Length == 0)
            return false;

        keyword = category + ":" + name;
        return true;
    }
}
