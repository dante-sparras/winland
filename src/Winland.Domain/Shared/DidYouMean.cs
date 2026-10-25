namespace Winland.Domain;

/// <summary>The hint an unknown config name gets when it is a typo of exactly one name Winland knows.</summary>
internal static class DidYouMean
{
    private const int MaxDistance = 2;

    /// <summary>
    /// <c> Did you mean 'name'?</c> when exactly one of <paramref name="known"/> is within two edits of
    /// <paramref name="name"/>, ignoring case, and otherwise empty. Two close names would make the hint a guess.
    /// </summary>
    public static string For(string name, IEnumerable<string> known)
    {
        string? match = null;
        foreach (var candidate in known)
        {
            if (Distance(name, candidate) > MaxDistance)
                continue;

            if (match is not null)
                return "";
            match = candidate;
        }

        return match is null ? "" : $" Did you mean '{match}'?";
    }

    // Levenshtein distance, row by row. Case does not count.
    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > MaxDistance)
            return MaxDistance + 1;

        var row = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            row[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                var cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
                row[j] = Math.Min(Math.Min(row[j - 1], above) + 1, diagonal + cost);
                diagonal = above;
            }
        }

        return row[b.Length];
    }
}
