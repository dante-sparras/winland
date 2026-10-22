namespace Winland.Domain;

/// <summary>
/// The Props of each Client and what decides them: the window rules it was adopted with, the property effects its
/// rules give it now, what <c>setprop</c> set, its tags and content type. A Prop is what setprop set, else what a
/// window rule gives, else what the Workspace's rule gives. The config's value below that is the caller's.
/// </summary>
internal sealed class ClientProps
{
    private readonly Dictionary<WindowId, Entry> _entries = [];

    /// <summary>The windows that have size rules or limits recorded.</summary>
    public IEnumerable<WindowId> Sized => _entries.Where(pair => pair.Value.Sizes is not null).Select(pair => pair.Key);

    /// <summary>The exec rules each adopted window follows, for the windows that follow any.</summary>
    public IEnumerable<IReadOnlyList<WindowRule>> Followed =>
        _entries.Values.Select(entry => entry.Subject?.Followed).OfType<IReadOnlyList<WindowRule>>();

    /// <summary>A window was adopted with <paramref name="decision"/>, from <paramref name="traits"/> and the exec rules it follows.</summary>
    public void Opened(WindowId window, WindowTraits traits, IReadOnlyList<WindowRule> followed, WindowDecision decision)
    {
        ArgumentNullException.ThrowIfNull(followed);
        var entry = Of(window);
        entry.Focus = new FocusRule(decision.NoInitialFocus, decision.NoFocus, decision.StayFocused);
        entry.Opacity = new OpacityChoice(decision.Opacity, decision.Opaque);
        entry.RuleProps = RulePropsOf(decision);
        entry.Subject = (traits.WithInitialNames(), followed);
        entry.Content = decision.Content;
    }

    /// <summary>The group effects a window's rules gave it when it opened.</summary>
    public void SetGroupRules(WindowId window, GroupRuleFlags rules) => Of(window).GroupRules = rules;

    /// <summary>The window's rules were matched again against its state: its property effects and tags follow.</summary>
    public void Rematched(WindowId window, WindowDecision decision, SortedSet<string> tags)
    {
        var entry = Of(window);
        entry.RuleProps = RulePropsOf(decision);
        entry.Opacity = new OpacityChoice(decision.Opacity, decision.Opaque);
        if (entry.Focus is FocusRule focus)
            entry.Focus = focus with { NoFocus = decision.NoFocus, StayFocused = decision.StayFocused };
        entry.RuleTags = tags;
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

    /// <summary>tagwindow: applies a static tag. True when the tags changed, so the rules give theirs again.</summary>
    public bool Tag(WindowId window, string tag)
    {
        var entry = Of(window);
        entry.StaticTags ??= new SortedSet<string>(StringComparer.Ordinal);
        if (!WindowTags.Apply(entry.StaticTags, tag, dynamic: false))
            return false;

        entry.RuleTags = null;
        return true;
    }

    /// <summary>cleartags: every tag goes, and the rules give theirs again when matched.</summary>
    public void ClearTags(WindowId window)
    {
        if (!_entries.TryGetValue(window, out var entry))
            return;

        entry.StaticTags = null;
        entry.RuleTags = null;
    }

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

    /// <summary>What a window rule, or else <paramref name="workspace"/>'s rule, gives the Prop. Null when neither gives it anything.</summary>
    public object? Ruled(WindowId window, string key, WorkspaceRule? workspace)
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
            "stay_focused" when focus.StayFocused => true,
            "opaque" when opacity.Opaque => true,
            "no_max_size" when sizes.NoMax => true,
            "min_size" when sizes.Min is Size min => new PropSize(min.Width, min.Height),
            "max_size" when sizes.Max is Size max => new PropSize(max.Width, max.Height),
            "opacity" when opacity.Rule is OpacityRule active => new PropAlpha(active.Active.Value, active.Active.Overridden),
            "opacity_inactive" when opacity.Rule is OpacityRule inactive => new PropAlpha(inactive.Inactive.Value, inactive.Inactive.Overridden),
            "opacity_fullscreen" when opacity.Rule is OpacityRule full => new PropAlpha(full.Fullscreen.Value, full.Fullscreen.Overridden),
            "border_size" when workspace?.BorderSize is int border => (long)border,
            "rounding" when workspace?.Rounding == false => 0L,
            "decorate" when workspace?.Decorate is bool decorate => decorate,
            _ => null,
        };
    }

    /// <summary>What setprop set, else what the rules give. Null when nothing does, and the config decides.</summary>
    public object? Value(WindowId window, string key, WorkspaceRule? workspace) =>
        SetProp(window, key) ?? Ruled(window, key, workspace);

    /// <summary>The focus effects the window's rules gave it, or null for a window that was never adopted.</summary>
    public FocusRule? FocusRuleOf(WindowId window) => _entries.TryGetValue(window, out var entry) ? entry.Focus : null;

    /// <summary>no_focus: what setprop set, else the rule's.</summary>
    public bool NoFocus(WindowId window) => SetBool(window, "no_focus") ?? FocusRuleOf(window)?.NoFocus ?? false;

    /// <summary>stay_focused: what setprop set, else the rule's.</summary>
    public bool StaysFocused(WindowId window) => SetBool(window, "stay_focused") ?? FocusRuleOf(window)?.StayFocused ?? false;

    /// <summary>The opacity a window's Props ask for: setprop's fractions over the opacity rule's.</summary>
    public OpacityChoice Opacity(WindowId window)
    {
        if (!_entries.TryGetValue(window, out var entry))
            return default;

        var ruled = entry.Opacity;
        var props = entry.SetProps;
        if (props.Count == 0)
            return ruled;

        OpacityFactor Factor(string key, OpacityFactor fallback) =>
            props.TryGetValue(key, out var value) && value is PropAlpha alpha ? new OpacityFactor(alpha.Value, alpha.Overridden) : fallback;

        var rule = ruled.Rule ?? new OpacityRule(OpacityFactor.Identity, OpacityFactor.Identity, OpacityFactor.Identity);
        var merged = new OpacityRule(Factor("opacity", rule.Active), Factor("opacity_inactive", rule.Inactive), Factor("opacity_fullscreen", rule.Fullscreen));
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
        NoMax = SetBool(window, "no_max_size") ?? rule.NoMax,
    };

    /// <summary>The window's tags: tagwindow's and what its rules give, as last matched.</summary>
    public IReadOnlyList<string> Tags(WindowId window) =>
        _entries.TryGetValue(window, out var entry) && (entry.RuleTags ?? entry.StaticTags) is { } tags ? [.. tags] : [];

    /// <summary>A copy of the tags tagwindow gave the window, which the rules' tags are matched on top of.</summary>
    public SortedSet<string> StaticTags(WindowId window) =>
        _entries.TryGetValue(window, out var entry) && entry.StaticTags is { } tags
            ? new SortedSet<string>(tags, StringComparer.Ordinal)
            : new SortedSet<string>(StringComparer.Ordinal);

    /// <summary>The window's tags as last matched, or tagwindow's when it was not matched since.</summary>
    public IReadOnlySet<string>? MatchedTags(WindowId window) =>
        _entries.TryGetValue(window, out var entry) ? entry.RuleTags ?? entry.StaticTags : null;

    /// <summary>The content type a content rule gave the window when it opened.</summary>
    public int Content(WindowId window) => _entries.TryGetValue(window, out var entry) ? entry.Content : 0;

    public GroupRuleFlags GroupRules(WindowId window) => _entries.TryGetValue(window, out var entry) ? entry.GroupRules : GroupRuleFlags.None;

    /// <summary>The traits and exec rules a window was adopted with, which its rules are matched against again.</summary>
    public (WindowTraits Traits, IReadOnlyList<WindowRule> Followed)? Subject(WindowId window) =>
        _entries.TryGetValue(window, out var entry) ? entry.Subject : null;

    private Entry Of(WindowId window)
    {
        if (!_entries.TryGetValue(window, out var entry))
            _entries[window] = entry = new Entry();

        return entry;
    }

    // A fullscreen_state rule turns sync_fullscreen off at the window-rule layer. The rule props are rebuilt whenever the
    // window's state changes, so the same false is put back while that rule still matches. setprop is a higher layer.
    private static IReadOnlyDictionary<string, object>? RulePropsOf(WindowDecision decision)
    {
        var props = decision.Props;
        if (decision.StatedFullscreen)
        {
            var ruled = new Dictionary<string, object>(props?.Count ?? 0, StringComparer.Ordinal);
            if (props is not null)
            {
                foreach (var (key, value) in props)
                    ruled[key] = value;
            }

            ruled["sync_fullscreen"] = false;
            props = ruled;
        }

        return props is { Count: > 0 } ? props : null;
    }

    private sealed class Entry
    {
        public Dictionary<string, object> SetProps { get; } = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, object>? RuleProps { get; set; }

        public FocusRule? Focus { get; set; }

        public OpacityChoice Opacity { get; set; }

        public (SizeRule Rule, Size? OwnMin, Size? OwnMax)? Sizes { get; set; }

        public (WindowTraits Traits, IReadOnlyList<WindowRule> Followed)? Subject { get; set; }

        public SortedSet<string>? StaticTags { get; set; }

        public SortedSet<string>? RuleTags { get; set; }

        public int Content { get; set; }

        public GroupRuleFlags GroupRules { get; set; }
    }
}

/// <summary>The focus effects a window's rules give it.</summary>
internal readonly record struct FocusRule(bool NoInitialFocus, bool NoFocus, bool StayFocused);

/// <summary>An opacity rule and whether the window is opaque.</summary>
internal readonly record struct OpacityChoice(OpacityRule? Rule, bool Opaque);
