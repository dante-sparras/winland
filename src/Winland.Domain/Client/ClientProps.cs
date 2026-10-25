namespace Winland.Domain;

/// <summary>
/// The Props of each Client and what decides them: the window rules it was adopted with, the property effects its
/// rules give it now, and what <c>setprop</c> set. A Prop is what setprop set, else what a window rule gives. The
/// config's value below that is the caller's.
/// </summary>
internal sealed class ClientProps
{
    private readonly Dictionary<WindowId, Entry> _entries = [];

    /// <summary>The exec rules each adopted window follows, for the windows that follow any.</summary>
    public IEnumerable<IReadOnlyList<WindowRule>> Followed =>
        _entries.Values.Select(entry => entry.Subject?.Followed).OfType<IReadOnlyList<WindowRule>>();

    /// <summary>A window was adopted with <paramref name="decision"/>, from <paramref name="traits"/> and the exec rules it follows.</summary>
    public void Opened(WindowId window, WindowTraits traits, IReadOnlyList<WindowRule> followed, WindowDecision decision)
    {
        ArgumentNullException.ThrowIfNull(followed);
        var entry = Of(window);
        entry.Focus = new FocusRule(decision.NoInitialFocus, decision.NoFocus);
        entry.Opacity = new OpacityChoice(decision.Opacity, decision.Opaque);
        entry.RuleProps = RulePropsOf(decision);
        entry.Subject = (traits.WithInitialNames(), followed);
    }

    /// <summary>The window's rules were matched again after its class or title changed: its property effects follow.</summary>
    public void Rematched(WindowId window, WindowDecision decision)
    {
        var entry = Of(window);
        entry.RuleProps = RulePropsOf(decision);
        entry.Opacity = new OpacityChoice(decision.Opacity, decision.Opaque);
        if (entry.Focus is FocusRule focus)
            entry.Focus = focus with { NoFocus = decision.NoFocus };
    }

    /// <summary>What setprop set, or with null what it set is dropped.</summary>
    public void Set(WindowId window, string key, object? value)
    {
        var props = Of(window).SetProps;
        if (value is null)
            props.Remove(key);
        else
            props[key] = value;
    }

    /// <summary>The size rules a window opened with and its own limits, so its limits can be worked out again.</summary>
    public void SetSizes(WindowId window, SizeRule rule, Size? ownMin, Size? ownMax) => Of(window).Sizes = (rule, ownMin, ownMax);

    public void Forget(WindowId window) => _entries.Remove(window);

    /// <summary>What setprop set.</summary>
    public object? SetProp(WindowId window, string key) =>
        _entries.TryGetValue(window, out var entry) && entry.SetProps.TryGetValue(key, out var value) ? value : null;

    public bool? SetBool(WindowId window, string key) => SetProp(window, key) as bool?;

    /// <summary>What the property effects of the window's rules give.</summary>
    public object? RuleProp(WindowId window, string key) =>
        _entries.TryGetValue(window, out var entry) && entry.RuleProps is { } props && props.TryGetValue(key, out var value) ? value : null;

    /// <summary>What setprop or a window rule set, which wins over the Workspace's rule and the config.</summary>
    public object? Own(WindowId window, string key) => SetProp(window, key) ?? RuleProp(window, key);

    /// <summary>The same, typed: null when neither setprop nor a window rule set it.</summary>
    public T Own<T>(WindowId window, WindowProp<T> prop)
    {
        ArgumentNullException.ThrowIfNull(prop);
        return Own(window, prop.Name) is T value ? value : default!;
    }

    /// <summary>What a window rule gives the Prop. Null when it gives it nothing.</summary>
    public object? Ruled(WindowId window, string key)
    {
        if (RuleProp(window, key) is { } ruled)
            return ruled;

        _entries.TryGetValue(window, out var entry);
        var focus = entry?.Focus ?? default;
        var sizes = entry?.Sizes?.Rule ?? default;
        var opacity = entry?.Opacity ?? default;
        return key switch
        {
            "no_focus" when focus.NoFocus => true,
            "opaque" when opacity.Opaque => true,
            "min_size" when sizes.Min is Size min => new PropSize(min.Width, min.Height),
            "max_size" when sizes.Max is Size max => new PropSize(max.Width, max.Height),
            "opacity" when opacity.Rule is OpacityRule active => new PropAlpha(active.Active),
            "opacity_inactive" when opacity.Rule is OpacityRule inactive => new PropAlpha(inactive.Inactive),
            _ => null,
        };
    }

    /// <summary>What setprop set, else what the rules give. Null when nothing does, and the config decides.</summary>
    public object? Value(WindowId window, string key) =>
        SetProp(window, key) ?? Ruled(window, key);

    /// <summary>The focus effects the window's rules gave it, or null for a window that was never adopted.</summary>
    public FocusRule? FocusRuleOf(WindowId window) => _entries.TryGetValue(window, out var entry) ? entry.Focus : null;

    /// <summary>no_focus: what setprop set, else the rule's.</summary>
    public bool NoFocus(WindowId window) => SetBool(window, "no_focus") ?? FocusRuleOf(window)?.NoFocus ?? false;

    /// <summary>The opacity a window's Props ask for: setprop's fractions over the opacity rule's.</summary>
    public OpacityChoice Opacity(WindowId window)
    {
        if (!_entries.TryGetValue(window, out var entry))
            return default;

        var ruled = entry.Opacity;
        var props = entry.SetProps;
        if (props.Count == 0)
            return ruled;

        float Factor(string key, float fallback) =>
            props.TryGetValue(key, out var value) && value is PropAlpha alpha ? alpha.Value : fallback;

        var rule = ruled.Rule ?? new OpacityRule(1f, 1f, 1f);
        var merged = rule with { Active = Factor("opacity", rule.Active), Inactive = Factor("opacity_inactive", rule.Inactive) };
        var opaque = props.TryGetValue("opaque", out var held) && held is bool flag ? flag : ruled.Opaque;
        return new OpacityChoice(ruled.Rule is null && merged == rule ? null : merged, opaque);
    }

    /// <summary>The size rules a window opened with and its own limits, or null when none are recorded.</summary>
    public (SizeRule Rule, Size? OwnMin, Size? OwnMax)? Sizes(WindowId window) =>
        _entries.TryGetValue(window, out var entry) ? entry.Sizes : null;

    /// <summary>The size rules with what setprop set on top.</summary>
    public SizeRule EffectiveSizes(WindowId window, SizeRule rule) => rule with
    {
        Min = (SetProp(window, "min_size") as PropSize?)?.ToLimit() ?? rule.Min,
        Max = (SetProp(window, "max_size") as PropSize?)?.ToLimit() ?? rule.Max,
    };

    /// <summary>The traits and exec rules a window was adopted with, which its rules are matched against again.</summary>
    public (WindowTraits Traits, IReadOnlyList<WindowRule> Followed)? Subject(WindowId window) =>
        _entries.TryGetValue(window, out var entry) ? entry.Subject : null;

    private Entry Of(WindowId window)
    {
        if (!_entries.TryGetValue(window, out var entry))
            _entries[window] = entry = new Entry();

        return entry;
    }

    private static IReadOnlyDictionary<string, object>? RulePropsOf(WindowDecision decision) =>
        decision.Props is { Count: > 0 } props ? props : null;

    private sealed class Entry
    {
        public Dictionary<string, object> SetProps { get; } = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, object>? RuleProps { get; set; }

        public FocusRule? Focus { get; set; }

        public OpacityChoice Opacity { get; set; }

        public (SizeRule Rule, Size? OwnMin, Size? OwnMax)? Sizes { get; set; }

        public (WindowTraits Traits, IReadOnlyList<WindowRule> Followed)? Subject { get; set; }

    }
}

/// <summary>The focus effects a window's rules give it.</summary>
internal readonly record struct FocusRule(bool NoInitialFocus, bool NoFocus);

/// <summary>An opacity rule and whether the window is opaque.</summary>
internal readonly record struct OpacityChoice(OpacityRule? Rule, bool Opaque);
