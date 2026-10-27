using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Winland.Domain;

/// <summary>
/// A pattern that must match a whole field, built once. Rules match on every window that appears and on every
/// title change, so each pattern is parsed only the first time. A pattern the linear engine can run never
/// backtracks, so a careless one cannot hold the core. Lookarounds and backreferences fall back to the
/// backtracking engine, under a timeout.
/// </summary>
internal static class WholeMatch
{
    private const int Limit = 512;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    private static readonly ConcurrentDictionary<string, Regex> Built = new(StringComparer.Ordinal);

    /// <summary>The pattern anchored to the whole field. Throws <see cref="ArgumentException"/> when it is not valid.</summary>
    public static Regex Of(string pattern)
    {
        if (Built.TryGetValue(pattern, out var regex))
            return regex;

        regex = Build($"\\A(?:{pattern})\\z");
        if (Built.Count >= Limit)
            Built.Clear();

        return Built.GetOrAdd(pattern, regex);
    }

    /// <summary>Whether <paramref name="text"/> matches. An invalid pattern, or one that runs out of time, does not.</summary>
    public static bool IsMatch(string pattern, string text)
    {
        try
        {
            return Of(pattern).IsMatch(text);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static Regex Build(string anchored)
    {
        try
        {
            return new Regex(anchored, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Timeout);
        }
        catch (NotSupportedException)
        {
            return new Regex(anchored, RegexOptions.CultureInvariant, Timeout);
        }
    }
}
