namespace Winland;

// The tab bar over each shown Group, and the border colors of grouped windows.
public sealed partial class Core
{
    /// <summary>The bars of the Groups shown now. A tiled Group's bar takes the top of its tile; a floating Group's sits above it.</summary>
    private List<GroupBar> GroupBars(State snapshot)
    {
        var bars = new List<GroupBar>();
        var style = _groupOptions.Bar;
        if (!style.Enabled)
            return bars;

        var clients = new Dictionary<WindowId, Client>(snapshot.Clients.Count);
        foreach (var client in snapshot.Clients)
            clients[client.Window] = client;

        var active = ActiveWindow();
        foreach (var desk in _workspaces)
        {
            if (desk.Groups.Count == 0 || !IsShown(desk.Id))
                continue;

            var scaled = style.ScaledBy(MonitorById(desk.Monitor)?.Effective.Scale ?? 1);

            // The bar's own font_family wins, and misc:font_family stands in for an empty one, as in the pinned release.
            scaled = scaled with { FontFamily = FontNames.Windows(scaled.FontFamily.Length == 0 ? _session.FontFamily : scaled.FontFamily) };
            var showing = desk.Showing();
            foreach (var group in desk.Groups)
            {
                if (style.DisableWhenOnly && group.Count == 1)
                    continue;

                if (!clients.TryGetValue(group.Shown, out var shown)
                    || shown.Minimized
                    || shown.Fullscreen == FullscreenMode.Fullscreen
                    || !showing.Contains(group.Shown))
                    continue;

                var reserved = scaled.ReservedFor(group.Count);
                var box = shown.Bounds;
                var strip = shown.Floating
                    ? new Rectangle(box.X, box.Y - reserved, box.Width, reserved)
                    : new Rectangle(box.X, box.Y, box.Width, Math.Min(reserved, box.Height));
                var tabs = new GroupTab[group.Count];
                for (var i = 0; i < group.Count; i++)
                {
                    var member = group.Members[i];
                    var title = clients.TryGetValue(member, out var client) ? client.Title : "";
                    tabs[i] = new GroupTab(member, title, member == active);
                }

                bars.Add(new GroupBar(group.Shown, strip, tabs, group.Locked || _groupsLocked, scaled));
            }
        }

        return bars;
    }

    /// <summary>Moves a tiled Group's shown member below its bar.</summary>
    private static IReadOnlyList<Placement> BelowBars(IReadOnlyList<Placement> placements, State snapshot, List<GroupBar> bars)
    {
        if (bars.Count == 0 || placements.Count == 0)
            return placements;

        var strips = new Dictionary<ClientId, int>(bars.Count);
        foreach (var bar in bars)
            strips[new ClientId(bar.Shown.Value)] = bar.Bounds.Height;

        var floating = new HashSet<ClientId>();
        foreach (var client in snapshot.Clients)
        {
            // A maximized Group member keeps room for its bar even while it floats, as in the pinned release.
            if (client.Floating && client.Fullscreen != FullscreenMode.Maximized)
                floating.Add(client.Id);
        }

        var moved = new Placement[placements.Count];
        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            if (!strips.TryGetValue(placement.Client, out var height) || floating.Contains(placement.Client))
            {
                moved[i] = placement;
                continue;
            }

            var box = placement.Bounds;
            moved[i] = placement with
            {
                Bounds = new Rectangle(box.X, box.Y + height, box.Width, Math.Max(0, box.Height - height)),
            };
        }

        return moved;
    }

    // Every color of the border, at its angle: a setprop gradient, the configured gradient, or a Group's one color.
    private BorderGradient BorderGradientOf(WindowId window, bool active)
    {
        if (OwnProp(window, active ? "active_border_color" : "inactive_border_color") is BorderGradient { Colors.Count: > 0 } set)
            return set;

        var color = BorderOf(window, active, honorProps: false);
        if (WorkspaceContaining(window)?.GroupOf(window) is not null || RulesOf(window).HasFlag(GroupRuleFlags.Deny))
            return BorderGradient.Of(color);

        var gradient = active ? _decoration.ActiveBorderGradient : _decoration.InactiveBorderGradient;
        return gradient is { Colors.Count: > 0 } ? gradient : BorderGradient.Of(color);
    }

    private BorderColor BorderOf(WindowId window, bool active, bool honorProps = true)
    {
        // A border color setprop set is drawn by its first color. One with no colors falls back to the config's.
        if (honorProps && OwnProp(window, active ? "active_border_color" : "inactive_border_color") is BorderGradient { First: BorderColor set })
            return set;

        if (WorkspaceContaining(window)?.GroupOf(window) is WindowGroup group)
        {
            var locked = group.Locked || _groupsLocked;
            return active
                ? locked ? _groupOptions.LockedActiveBorder : _groupOptions.ActiveBorder
                : locked ? _groupOptions.LockedInactiveBorder : _groupOptions.InactiveBorder;
        }

        if (RulesOf(window).HasFlag(GroupRuleFlags.Deny))
            return active ? _groupOptions.NoGroupActiveBorder : _groupOptions.NoGroupBorder;

        return active ? _decoration.ActiveBorder : _decoration.InactiveBorder;
    }
}
