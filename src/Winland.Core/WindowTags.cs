namespace Winland;

/// <summary>
/// A window's tags, kept as the pinned release keeps them. <c>+tag</c> sets a tag, <c>-tag</c> clears it, and a bare
/// tag toggles it. A tag a window rule gives is dynamic and is kept with a trailing <c>*</c>, beside a static tag of
/// the same name that <c>tagwindow</c> gave.
/// </summary>
internal static class WindowTags
{
    /// <summary>True when the set holds the tag. <c>negative:</c> inverts it, and unless strict a dynamic tag counts.</summary>
    public static bool IsTagged(IReadOnlySet<string> tags, string tag, bool strict = false)
    {
        var negative = tag.StartsWith("negative", StringComparison.Ordinal);
        var match = negative ? (tag.Length > 9 ? tag[9..] : "") : tag;
        var tagged = tags.Contains(match) || (!strict && tags.Contains(match + "*"));
        return negative ? !tagged : tagged;
    }

    /// <summary>Applies one tag request to the set. True when the set changed.</summary>
    public static bool Apply(SortedSet<string> tags, string tag, bool dynamic)
    {
        var real = dynamic && !tag.EndsWith('*') ? tag + "*" : tag;
        bool changed;
        bool set;
        if (real.StartsWith('-'))
        {
            real = real[1..];
            changed = IsTagged(tags, real, strict: true);
            set = false;
        }
        else if (real.StartsWith('+'))
        {
            real = real[1..];
            changed = !IsTagged(tags, real, strict: true);
            set = true;
        }
        else
        {
            changed = true;
            set = !IsTagged(tags, real, strict: true);
        }

        if (!changed)
            return false;

        if (set)
            tags.Add(real);
        else
            tags.Remove(real);

        return true;
    }
}
