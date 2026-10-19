using System.Globalization;

namespace Winland;

internal static partial class WindowRuleParser
{
    // The matches on a window's state, by the pinned release's names and the older names a line config still uses.
    private static readonly Dictionary<string, RuleMatchKind> DynamicKeys = new(StringComparer.Ordinal)
    {
        ["float"] = RuleMatchKind.Float,
        ["floating"] = RuleMatchKind.Float,
        ["fullscreen"] = RuleMatchKind.Fullscreen,
        ["pin"] = RuleMatchKind.Pin,
        ["pinned"] = RuleMatchKind.Pin,
        ["focus"] = RuleMatchKind.Focus,
        ["group"] = RuleMatchKind.Group,
        ["modal"] = RuleMatchKind.Modal,
        ["fullscreen_state_internal"] = RuleMatchKind.FullscreenStateInternal,
        ["fullscreen_state_client"] = RuleMatchKind.FullscreenStateClient,
        ["workspace"] = RuleMatchKind.Workspace,
        ["onworkspace"] = RuleMatchKind.Workspace,
        ["tag"] = RuleMatchKind.Tag,
        ["content"] = RuleMatchKind.Content,
    };

    private static bool TryParseDynamicMatch(string text, out RuleMatch match)
    {
        match = null!;
        var colon = text.IndexOf(':');
        if (colon <= 0 || !DynamicKeys.TryGetValue(text[..colon], out var kind))
            return false;

        match = new RuleMatch(kind, text[(colon + 1)..].Trim());
        return true;
    }

    /// <summary>True when every match on the window's state holds. A rule with such matches needs a subject.</summary>
    private static bool MatchesState(WindowRule rule, RuleSubject? subject)
    {
        if (rule.Dynamic is not { Count: > 0 } matches)
            return true;

        if (subject is not RuleSubject state)
            return false;

        foreach (var match in matches)
        {
            var holds = match.Kind switch
            {
                RuleMatchKind.Float => WindowProps.Truthy(match.Value) == state.Floating,
                RuleMatchKind.Fullscreen => WindowProps.Truthy(match.Value) == (state.FullscreenInternal != 0),
                RuleMatchKind.Pin => WindowProps.Truthy(match.Value) == state.Pinned,
                RuleMatchKind.Focus => WindowProps.Truthy(match.Value) == state.Focused,
                RuleMatchKind.Group => WindowProps.Truthy(match.Value) == state.Grouped,
                RuleMatchKind.Modal => WindowProps.Truthy(match.Value) == state.Modal,
                RuleMatchKind.FullscreenStateInternal => LeadingInteger(match.Value) == state.FullscreenInternal,
                RuleMatchKind.FullscreenStateClient => LeadingInteger(match.Value) == state.FullscreenClient,
                RuleMatchKind.Workspace => state.OnWorkspace(match.Value),
                RuleMatchKind.Tag => state.Tagged(match.Value),
                // As the pinned release reads it, the pattern matches the type's number or its name.
                RuleMatchKind.Content => Matches(match.Value, state.Content.ToString(CultureInfo.InvariantCulture))
                    || Matches(match.Value, ContentTypes.Name(state.Content)),
                _ => false,
            };

            if (!holds)
                return false;
        }

        return true;
    }

    // A border_color rule that matches on focus gives its one gradient to the border that focus shows, as in the
    // pinned release: focus:1 with only an inactive color colors the active border, focus:0 with only an active
    // color the inactive one.
    private static void FocusBorderColor(WindowRule rule, Dictionary<string, object> props)
    {
        if (rule.Dynamic is null)
            return;

        var hasActive = rule.Props!.TryGetValue("active_border_color", out var active);
        var hasInactive = rule.Props.TryGetValue("inactive_border_color", out var inactive);
        foreach (var match in rule.Dynamic)
        {
            if (match.Kind != RuleMatchKind.Focus)
                continue;

            if (WindowProps.Truthy(match.Value) && hasInactive && !hasActive)
                props["active_border_color"] = inactive!;
            else if (!WindowProps.Truthy(match.Value) && hasActive && !hasInactive)
                props["inactive_border_color"] = active!;
        }
    }

    /// <summary>The tag requests of the rules that match the window, in the order the rules come.</summary>
    internal static List<string> TagsFor(WindowTraits traits, IReadOnlyList<WindowRule> rules, RuleSubject subject)
    {
        var tags = new List<string>();
        foreach (var rule in rules)
        {
            if (rule.Effect == WindowRuleEffect.Tag && rule.Text is string tag && Matches(rule, traits) && MatchesState(rule, subject))
                tags.Add(tag);
        }

        return tags;
    }

    /// <summary>True when the rule reads a window's state, so it is checked again as that state changes.</summary>
    internal static bool ReadsState(WindowRule rule) => rule.Dynamic is { Count: > 0 } || rule.Effect == WindowRuleEffect.Tag;

    /// <summary>True when the rule's effect is one the pinned release applies again while the window lives.</summary>
    internal static bool IsDynamicEffect(WindowRule rule) => rule.Effect is WindowRuleEffect.Prop
        or WindowRuleEffect.NoFocus
        or WindowRuleEffect.StayFocused
        or WindowRuleEffect.MinSize
        or WindowRuleEffect.MaxSize
        or WindowRuleEffect.NoMaxSize
        or WindowRuleEffect.Opacity
        or WindowRuleEffect.Opaque
        or WindowRuleEffect.IdleInhibit;

    // The leading whole number of the text, as std::stoi reads it, or 0 when there is none.
    private static int LeadingInteger(string text)
    {
        var trimmed = text.TrimStart();
        var end = 0;
        if (end < trimmed.Length && trimmed[end] is '+' or '-')
            end++;
        while (end < trimmed.Length && char.IsAsciiDigit(trimmed[end]))
            end++;

        return int.TryParse(trimmed[..end], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}
