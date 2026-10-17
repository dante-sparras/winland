namespace Winland;

// Window rules that match on a window's state, such as float:0 or workspace:w[tv1]. The pinned release matches the
// property effects of its rules again whenever a window's state changes, so these follow the window as it floats,
// gains focus, goes fullscreen, joins a Group or moves between Workspaces.
public sealed partial class Core
{
    // What each window was adopted with: its traits and the exec rules that followed its process.
    private readonly Dictionary<WindowId, (WindowTraits Traits, IReadOnlyList<WindowRule> Followed)> _ruleSubjects = [];

    // The tags tagwindow gave each window, and each window's tags with what its rules give added.
    private readonly Dictionary<WindowId, SortedSet<string>> _staticTags = [];
    private readonly Dictionary<WindowId, SortedSet<string>> _ruleTags = [];

    // Whether the last pass found a rule that reads state.
    private bool _matchedState;

    private IReadOnlyList<string> TagsOf(WindowId window) =>
        _ruleTags.TryGetValue(window, out var tags) || _staticTags.TryGetValue(window, out tags) ? [.. tags] : [];

    // The content type a content window rule gave each window when it opened.
    private readonly Dictionary<WindowId, int> _content = [];

    private int ContentOf(WindowId window) => _content.TryGetValue(window, out var content) ? content : 0;

    private void RememberRuleSubject(WindowId window, WindowTraits traits, IReadOnlyList<WindowRule> followed, WindowDecision decision)
    {
        _ruleSubjects[window] = (traits, followed);
        if (decision.Content != 0)
            _content[window] = decision.Content;
    }

    // Matches the tag, property, opacity, focus and idle effects of every window's rules against its state now. The placements of each
    // shown Workspace where a window's border changed, laid out again, and whether any window's tags changed.
    private List<Placement> MatchRulesAgain(out bool tagsChanged)
    {
        tagsChanged = false;
        var placements = new List<Placement>();
        // Once no rule reads state, one more pass puts back what the rules give a window when it opens.
        var reads = ReadsState(_rules) || FollowedReadState();
        if (!reads && !_matchedState)
            return placements;

        _matchedState = reads;

        var active = ActiveWindow();
        foreach (var desk in _workspaces)
        {
            var changed = false;
            foreach (var client in desk.Layout.Clients)
            {
                if (!_ruleSubjects.TryGetValue(client.Window, out var kept))
                    continue;

                var traits = kept.Traits with { Class = client.Class, Title = client.Title };
                var rules = JoinRules(_rules, kept.Followed);
                var tags = _staticTags.TryGetValue(client.Window, out var own)
                    ? new SortedSet<string>(own, StringComparer.Ordinal)
                    : new SortedSet<string>(StringComparer.Ordinal);
                foreach (var tag in WindowRuleParser.TagsFor(traits, rules, SubjectOf(desk, client, active, tags)))
                    _ = WindowTags.Apply(tags, tag, dynamic: true);

                var before = _ruleTags.TryGetValue(client.Window, out var held) ? held : own;
                tagsChanged |= !(before ?? []).SetEquals(tags);
                _ruleTags[client.Window] = tags;

                var decision = WindowRuleParser.Decide(traits, rules, SubjectOf(desk, client, active, tags));
                var border = RulePropOf(client.Window, "border_size");
                RememberRuleProps(client.Window, decision);
                RememberOpacity(client.Window, decision);
                RememberIdle(client.Window, decision);
                if (_focusRules.TryGetValue(client.Window, out var focus))
                    _focusRules[client.Window] = focus with { NoFocus = decision.NoFocus, StayFocused = decision.StayFocused };
                changed |= !Equals(border, RulePropOf(client.Window, "border_size"));
            }

            if (changed && IsShown(desk.Id))
                placements.AddRange(desk.Layout.Apply(desk.Options));
        }

        return placements;
    }

    private RuleSubject SubjectOf(LiveWorkspace desk, Client client, WindowId? active, IReadOnlySet<string> tags)
    {
        var screen = MonitorById(desk.Monitor);
        int Count(WindowCount wanted) => desk.CountWindows(wanted, _pinned);
        return new RuleSubject(
            client.Floating,
            (int)client.Fullscreen,
            (int)client.FullscreenClient,
            client.Floating && _pinned.Contains(client.Window),
            client.Window == active,
            desk.GroupOf(client.Window) is not null,
            selector => WorkspaceRules.MatchesSelector(selector, desk.Id, desk.DisplayName, screen?.Effective, Count, desk.CoveringMode()),
            tag => WindowTags.IsTagged(tags, tag),
            ContentOf(client.Window));
    }

    /// <summary><c>tagwindow tag [window]</c> applies a static tag to the window, or the focused one, as in the pinned release.</summary>
    private Outcome TagWindow(string arguments, bool allowsElevation)
    {
        const string name = "tagwindow";
        var text = arguments.Trim();
        var space = text.IndexOfAny([' ', '	']);
        var tag = space < 0 ? text : text[..space];
        var selector = space < 0 ? "" : text[(space + 1)..].Trim();
        if (tag.Length == 0 || Select(selector, emptyMeansActive: true) is not WindowId window)
            return View([], dispatcher: name, allowsElevation: allowsElevation);

        if (!_staticTags.TryGetValue(window, out var tags))
            _staticTags[window] = tags = new SortedSet<string>(StringComparer.Ordinal);

        // The rules are matched again, with the new tag, when this Outcome is decorated.
        if (WindowTags.Apply(tags, tag, dynamic: false))
            _ruleTags.Remove(window);

        return View([], dispatcher: name, allowsElevation: allowsElevation);
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
        foreach (var kept in _ruleSubjects.Values)
        {
            if (kept.Followed.Count > 0 && ReadsState(kept.Followed))
                return true;
        }

        return false;
    }
}
