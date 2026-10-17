namespace Winland;

// Window rules that match on a window's state, such as float:0 or workspace:w[tv1]. The pinned release matches the
// property effects of its rules again whenever a window's state changes, so these follow the window as it floats,
// gains focus, goes fullscreen, joins a Group or moves between Workspaces.
public sealed partial class Core
{
    // What each window was adopted with: its traits and the exec rules that followed its process.
    private readonly Dictionary<WindowId, (WindowTraits Traits, IReadOnlyList<WindowRule> Followed)> _ruleSubjects = [];

    private void RememberRuleSubject(WindowId window, WindowTraits traits, IReadOnlyList<WindowRule> followed) =>
        _ruleSubjects[window] = (traits, followed);

    // Matches the property effects of every window's rules against its state now. The placements of each shown
    // Workspace where a window's border changed, laid out again.
    private List<Placement> MatchRulesAgain()
    {
        var placements = new List<Placement>();
        if (!ReadsState(_rules) && !FollowedReadState())
            return placements;

        var active = ActiveWindow();
        foreach (var desk in _workspaces)
        {
            var changed = false;
            foreach (var client in desk.Layout.Clients)
            {
                if (!_ruleSubjects.TryGetValue(client.Window, out var kept))
                    continue;

                var traits = kept.Traits with { Class = client.Class, Title = client.Title };
                var decision = WindowRuleParser.Decide(traits, JoinRules(_rules, kept.Followed), SubjectOf(desk, client, active));
                var border = RulePropOf(client.Window, "border_size");
                RememberRuleProps(client.Window, decision);
                changed |= !Equals(border, RulePropOf(client.Window, "border_size"));
            }

            if (changed && IsShown(desk.Id))
                placements.AddRange(desk.Layout.Apply(desk.Options));
        }

        return placements;
    }

    private RuleSubject SubjectOf(LiveWorkspace desk, Client client, WindowId? active)
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
            selector => WorkspaceRules.MatchesSelector(selector, desk.Id, desk.DisplayName, screen?.Effective, Count, desk.CoveringMode()));
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
