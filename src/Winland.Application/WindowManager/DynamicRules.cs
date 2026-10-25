namespace Winland.Application;

// Window rules match what a window is: its class, title, exe and aumid. When a window's class or title changes, the
// property effects of its rules are matched again, so they follow the new name. Effects that apply only as the window
// opens, such as float and workspace, stay as they were.
public sealed partial class WindowManager
{
    // Windows whose class or title changed since their rules were last matched.
    private readonly HashSet<WindowId> _renamed = [];

    // Matches the property, opacity and focus effects of each renamed window's rules again. The placements of each
    // shown Workspace where a window's border changed, laid out again.
    private List<Placement> MatchRulesAgain()
    {
        var placements = new List<Placement>();
        if (_renamed.Count == 0)
            return placements;

        foreach (var desk in _workspaces)
        {
            var changed = false;
            foreach (var client in desk.Layout.Clients)
            {
                if (!_renamed.Contains(client.Window) || _props.Subject(client.Window) is not { } kept)
                    continue;

                var traits = kept.Traits with { Class = client.Class, Title = client.Title };
                var decision = WindowRuleParser.Decide(traits, JoinRules(_config.WindowRules, kept.Followed));
                var border = _props.RuleProp(client.Window, "border_size");
                _props.Rematched(client.Window, decision);
                changed |= !Equals(border, _props.RuleProp(client.Window, "border_size"));
            }

            if (changed && _workspaces.Shown.IsShown(desk.Id))
                placements.AddRange(desk.Layout.Apply(desk.Options));
        }

        _renamed.Clear();
        return placements;
    }
}
