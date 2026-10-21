namespace Winland.Domain;

/// <summary>
/// <c>animations:enabled</c>, <c>animations:workspace_wraparound</c>, the named <c>bezier</c> curves
/// and the <c>animation</c> tree, read in config order.
/// </summary>
public sealed record AnimationOptions
{
    private AnimationOptions()
    {
    }

    private AnimationBook Book { get; init; } = new();

    public static AnimationOptions Documented { get; } = new();

    public bool Enabled { get; init; } = true;

    public bool Wraparound { get; init; }

    internal static OptionSet<AnimationOptions> Options { get; } = new(
    [
        Option<AnimationOptions>.Of("animations:enabled", OptionValue.Bool, o => o.Enabled, (o, v) => o with { Enabled = v }),
        Option<AnimationOptions>.Of("animations:workspace_wraparound", OptionValue.Bool, o => o.Wraparound, (o, v) => o with { Wraparound = v }),
    ]);

    /// <summary>The animation tree and the curves, as the <c>animations</c> request answers.</summary>
    internal string Describe(bool json) => Book.Describe(json);

    public static AnimationOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var book = new AnimationBook();
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
            }
        }

        return Options.From(Documented with { Book = book }, document);
    }

    /// <summary>The resolved values of <paramref name="node"/>, inherited from the nearest set ancestor.</summary>
    public AnimationLeaf Leaf(string node) => Book.Effective(node);

    /// <summary>The curve and duration of <paramref name="node"/>, or null when it does not animate.</summary>
    public Timing? TimingOf(string node)
    {
        if (!Enabled)
            return null;

        var leaf = Book.Effective(node);
        if (!leaf.Enabled || !Book.TryCurve(leaf.Curve, out var curve))
            return null;

        // Speed is in deciseconds. A spring runs until it settles, whatever the speed.
        return curve is SpringCurve spring ? new Timing(spring, spring.Milliseconds) : new Timing(curve, leaf.Speed * 100);
    }

    /// <summary>The style of <paramref name="node"/>, lower case. Empty when none is set.</summary>
    public string StyleOf(string node) => Book.Effective(node).Style.ToLowerInvariant();
}
