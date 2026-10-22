namespace Winland.Application;

// Window rules that match on a window's state, such as float:0 or workspace:w[tv1]. Winland matches the
// property effects of its rules again whenever a window's state changes, so these follow the window as it floats,
// gains focus, goes fullscreen, joins a Group or moves between Workspaces.
public sealed partial class WindowManager
{
    // Whether the last pass found a rule that reads state.
    private bool _matchedState;

    // Matches the tag, property, opacity and focus effects of every window's rules against its state now. The placements of each
    // shown Workspace where a window's border changed, laid out again, and whether any window's tags changed.
    private List<Placement> MatchRulesAgain(out bool tagsChanged)
    {
        tagsChanged = false;
        var placements = new List<Placement>();
        // Once no rule reads state, one more pass puts back what the rules give a window when it opens.
        var reads = ReadsState(_config.WindowRules) || FollowedReadState();
        if (!reads && !_matchedState)
            return placements;

        _matchedState = reads;

        var active = ActiveWindow();
        foreach (var desk in _workspaces)
        {
            var changed = false;
            foreach (var client in desk.Layout.Clients)
            {
                if (_props.Subject(client.Window) is not { } kept)
                    continue;

                var traits = kept.Traits with { Class = client.Class, Title = client.Title };
                var rules = JoinRules(_config.WindowRules, kept.Followed);
                var tags = _props.StaticTags(client.Window);
                foreach (var tag in WindowRuleParser.TagsFor(traits, rules, SubjectOf(desk, client, active, tags)))
                    _ = WindowTags.Apply(tags, tag, dynamic: true);

                tagsChanged |= !(_props.MatchedTags(client.Window) ?? new HashSet<string>()).SetEquals(tags);
                var decision = WindowRuleParser.Decide(traits, rules, SubjectOf(desk, client, active, tags));
                var border = _props.RuleProp(client.Window, "border_size");
                _props.Rematched(client.Window, decision, tags);
                changed |= !Equals(border, _props.RuleProp(client.Window, "border_size"));
            }

            if (changed && IsShown(desk.Id))
                placements.AddRange(desk.Layout.Apply(desk.Options));
        }

        return placements;
    }

    // Winland reads a window's rules when it opens with the tags its rules give it and the Workspace it
    // opens on, so a rule with a state match, such as float, tag:x or workspace:3, applies then too. The
    // window is not yet floating, focused, pinned, fullscreen or in a Group. Where it opens stays as first decided.
    private WindowDecision DecideAtOpen(WindowTraits traits, IReadOnlyList<WindowRule> rules, WindowDecision decision, LiveWorkspace desk, bool modal)
    {
        if (!ReadsState(rules))
            return decision;

        var screen = MonitorById(desk.Monitor);
        int Count(WindowCount wanted) => desk.CountWindows(wanted, _pinned);
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        var opening = new RuleSubject(
            Floating: false,
            FullscreenInternal: 0,
            FullscreenClient: 0,
            Pinned: false,
            Focused: false,
            Grouped: false,
            OnWorkspace: selector => WorkspaceRules.MatchesSelector(selector, desk.Id, desk.DisplayName, screen?.Effective, Count, desk.CoveringMode()),
            Tagged: tag => WindowTags.IsTagged(tags, tag),
            Content: decision.Content,
            Modal: modal);
        foreach (var tag in WindowRuleParser.TagsFor(traits, rules, opening))
            _ = WindowTags.Apply(tags, tag, dynamic: true);

        var matched = WindowRuleParser.Decide(traits, rules, opening);
        return matched with { Workspace = decision.Workspace, Placement = matched.Placement with { Monitor = decision.Placement.Monitor } };
    }

    private RuleSubject SubjectOf(LiveWorkspace desk, Client client, WindowId? active, IReadOnlySet<string> tags)
    {
        var screen = MonitorById(desk.Monitor);
        int Count(WindowCount wanted) => desk.CountWindows(wanted, _pinned);
        return new RuleSubject(
            client.Floating,
            (int)client.Fullscreen,
            (int)client.FullscreenClient,
            IsPinned(client.Window),
            client.Window == active,
            desk.GroupOf(client.Window) is not null,
            selector => WorkspaceRules.MatchesSelector(selector, desk.Id, desk.DisplayName, screen?.Effective, Count, desk.CoveringMode()),
            tag => WindowTags.IsTagged(tags, tag),
            _props.Content(client.Window),
            IsModal(client.Window));
    }

    /// <summary><c>tagwindow tag [window]</c> applies a static tag to the window, or the focused one.</summary>
    private Outcome TagWindow(string arguments)
    {
        const string name = "tagwindow";
        var text = arguments.Trim();
        var space = text.IndexOfAny([' ', '	']);
        var tag = space < 0 ? text : text[..space];
        var selector = space < 0 ? "" : text[(space + 1)..].Trim();
        if (tag.Length == 0 || Select(selector, emptyMeansActive: true) is not WindowId window)
            return View([], dispatcher: name);

        // The rules are matched again, with the new tag, when this Outcome is decorated.
        _ = _props.Tag(window, tag);

        return View([], dispatcher: name);
    }

    /// <summary>
    /// <c>cleartags [window]</c> takes every tag off the window, or the focused one, as Winland's clear_tags
    /// does. The rules are matched again, so a tag a rule gives comes back.
    /// </summary>
    private Outcome ClearTags(string arguments)
    {
        const string name = "cleartags";
        if (Select(arguments.Trim(), emptyMeansActive: true) is WindowId window)
            _props.ClearTags(window);

        return View([], dispatcher: name);
    }

    private static bool ReadsState(IReadOnlyList<WindowRule> rules)
    {
        foreach (var rule in rules)
        {
            if (WindowRuleParser.ReadsState(rule))
                return true;
        }

        return false;
    }

    private bool FollowedReadState()
    {
        foreach (var followed in _props.Followed)
        {
            if (followed.Count > 0 && ReadsState(followed))
                return true;
        }

        return false;
    }
}
