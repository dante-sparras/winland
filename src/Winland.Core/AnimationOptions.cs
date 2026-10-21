namespace Winland;

/// <summary>How long one animation runs and how it eases.</summary>
public readonly record struct Timing(Easing Curve, double Milliseconds);

/// <summary>The values one node of the animation tree resolves to, after inheritance.</summary>
public sealed record AnimationLeaf(bool Enabled, double Speed, string Curve, string Style);

/// <summary>
/// <c>animations:enabled</c>, <c>animations:workspace_wraparound</c>, the named <c>bezier</c> curves
/// and the <c>animation</c> tree, read in config order.
/// </summary>
public sealed class AnimationOptions
{
    internal static IReadOnlySet<string> Keywords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "animations:enabled",
        "animations:workspace_wraparound",
    };

    private readonly AnimationBook _book;

    private AnimationOptions(AnimationBook book, bool enabled, bool wraparound)
    {
        _book = book;
        Enabled = enabled;
        Wraparound = wraparound;
    }

    public static AnimationOptions Documented { get; } = new(new AnimationBook(), enabled: true, wraparound: false);

    public bool Enabled { get; }

    public bool Wraparound { get; }

    /// <summary>The animation tree and the curves, as the <c>animations</c> request answers.</summary>
    internal string Describe(bool json) => _book.Describe(json);

    public static AnimationOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var book = new AnimationBook();
        var enabled = true;
        var wraparound = false;
        foreach (var setting in document.Settings)
        {
            switch (setting.Keyword)
            {
                case AnimationBook.BezierKeyword:
                    _ = book.AddBezier(setting.Value);
                    break;
                case AnimationBook.SpringKeyword:
                    _ = book.AddSpring(setting.Value);
                    break;
                case AnimationBook.AnimationKeyword:
                    _ = book.SetAnimation(setting.Value);
                    break;
                case "animations:enabled" when ConfigValueSyntax.TryParseBool(setting.Value, out var on):
                    enabled = on;
                    break;
                case "animations:workspace_wraparound" when ConfigValueSyntax.TryParseBool(setting.Value, out var wrap):
                    wraparound = wrap;
                    break;
            }
        }

        return new AnimationOptions(book, enabled, wraparound);
    }

    /// <summary>The resolved values of <paramref name="node"/>, inherited from the nearest set ancestor.</summary>
    public AnimationLeaf Leaf(string node) => _book.Effective(node);

    /// <summary>The curve and duration of <paramref name="node"/>, or null when it does not animate.</summary>
    public Timing? TimingOf(string node)
    {
        if (!Enabled)
            return null;

        var leaf = _book.Effective(node);
        if (!leaf.Enabled || !_book.TryCurve(leaf.Curve, out var curve))
            return null;

        // Speed is in deciseconds. A spring runs until it settles, whatever the speed.
        return curve is SpringCurve spring ? new Timing(spring, spring.Milliseconds) : new Timing(curve, leaf.Speed * 100);
    }

    /// <summary>The style of <paramref name="node"/>, lower case. Empty when none is set.</summary>
    public string StyleOf(string node) => _book.Effective(node).Style.ToLowerInvariant();
}
