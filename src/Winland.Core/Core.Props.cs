namespace Winland;

/// <summary>
/// Window properties, as Winland's <c>getprop</c> and <c>setprop</c> reach them. A property's value is what
/// <c>setprop</c> set, else what a window rule or workspace rule gives, else the config's value.
/// </summary>
public sealed partial class Core
{
    // What setprop set on each window, by storage key. It wins over the rules and the config.
    private readonly Dictionary<WindowId, Dictionary<string, object>> _setProps = [];

    // What the property effects of the window rules gave each window when it was adopted.
    private readonly Dictionary<WindowId, IReadOnlyDictionary<string, object>> _ruleProps = [];

    private object? SetPropOf(WindowId window, string key) =>
        _setProps.TryGetValue(window, out var props) && props.TryGetValue(key, out var value) ? value : null;

    private object? RulePropOf(WindowId window, string key) =>
        _ruleProps.TryGetValue(window, out var props) && props.TryGetValue(key, out var value) ? value : null;

    // A property setprop or a window rule set, which wins over the workspace rules and the config.
    private object? OwnProp(WindowId window, string key) => SetPropOf(window, key) ?? RulePropOf(window, key);

    private void RememberRuleProps(WindowId window, WindowDecision decision)
    {
        var props = decision.Props;
        // Winland turns sync_fullscreen off at the window-rule layer when a fullscreen_state rule maps the
        // window. Rule props are rebuilt whenever the window's state changes, so the same false is put back while
        // that rule still matches. setprop is a higher layer and is left alone.
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

        if (props is { Count: > 0 })
            _ruleProps[window] = props;
        else
            _ruleProps.Remove(window);
    }

    // The border a window's own properties give it, which the Layouts read through LayoutOptions.ClientBorder.
    private int? ClientBorderOf(ClientId client) =>
        OwnProp(new WindowId(client.Value), "border_size") is long border ? (int)Math.Clamp(border, 0, int.MaxValue) : null;

    private bool? SetBool(WindowId window, string key) => SetPropOf(window, key) as bool?;

    // What a window rule or workspace rule gives the property, or null when none gives it anything.
    private object? RuledProp(WindowId window, LiveWorkspace? desk, string key)
    {
        var rule = _focusRules.TryGetValue(window, out var focus) ? focus : default;
        var sizes = _sizes.TryGetValue(window, out var kept) ? kept.Rule : default;
        var opacity = OpacityOf(window);
        var workspace = desk is null ? null : RuleFor(desk);
        if (RulePropOf(window, key) is { } ruled)
            return ruled;

        return key switch
        {
            "no_focus" when rule.NoFocus => true,
            "stay_focused" when rule.StayFocused => true,
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

    private object DefaultProp(WindowId window, string key, PropKind kind) => key switch
    {
        "animation" => "",
        "max_size" => PropSize.Unbounded,
        "min_size" => new PropSize(20, 20),
        "opacity" or "opacity_inactive" or "opacity_fullscreen" => PropAlpha.Identity,
        "active_border_color" => BorderOf(window, active: true, honorProps: false),
        "inactive_border_color" => BorderOf(window, active: false, honorProps: false),
        "decorate" or "sync_fullscreen" => true,
        "border_size" => (long)_options.BorderSize,
        "rounding" => (long)_decoration.Rounding,
        "rounding_power" => _decoration.RoundingPower,
        "scroll_mouse" => (float)(_input.ScrollFactor ?? 1),
        "scroll_touchpad" => (float)_input.TouchpadScrollFactor,
        _ when kind == PropKind.Bool => false,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No default for this property."),
    };

    private object Layered(WindowId window, LiveWorkspace? desk, string key) =>
        SetPropOf(window, key) ?? RuledProp(window, desk, key)!;

    private object PropValue(WindowId window, LiveWorkspace? desk, string key, PropKind kind) =>
        SetPropOf(window, key) ?? RuledProp(window, desk, key) ?? DefaultProp(window, key, kind);

    private CommandReply ReadProp(GetPropRequest request)
    {
        if (request.Selector.Length == 0 || request.Property.Length == 0)
            return new CommandReply("not enough args", View([]));

        if (!TrySelect(request.Selector, out var client, out var desk))
            return new CommandReply("window not found", View([]));

        if (!WindowProps.TryKind(request.Property, out var kind))
            return new CommandReply("prop not found", View([]));

        var key = WindowProps.StorageKey(request.Property);
        var value = PropValue(client.Window, desk, key, kind);
        var json = request.Flags.HasFlag(CommandFlags.Json);
        var text = WindowProps.Format(request.Property, kind, value, json, animationSet: SetPropOf(client.Window, key) is not null);
        return new CommandReply(text, View([]));
    }

    private CommandReply WriteProp(SetPropRequest request)
    {
        var outcome = SetProp(request.Selector, request.Property, request.Value, "setprop");
        return new CommandReply(outcome.Error ?? "ok", outcome);
    }

    /// <summary><c>setprop WINDOW PROP VALUE</c>, Winland's set_prop dispatcher.</summary>
    private Outcome SetPropDispatch(string arguments)
    {
        var text = arguments.Trim();
        var first = text.IndexOfAny([' ', '\t']);
        var selector = first < 0 ? text : text[..first];
        var rest = first < 0 ? "" : text[(first + 1)..].TrimStart();
        var second = rest.IndexOfAny([' ', '\t']);
        var property = second < 0 ? rest : rest[..second];
        var value = second < 0 ? "" : rest[(second + 1)..].Trim();
        return SetProp(selector, property, value, "setprop");
    }

    private Outcome SetProp(string selector, string property, string value, string dispatcher)
    {
        Outcome Refused(string error) => View([], dispatcher: dispatcher) with { Error = error };

        if (selector.Length == 0 || property.Length == 0 || value.Length == 0)
            return Refused("error: not enough args");

        if (!TrySelect(selector, out var client, out var desk) || desk is null)
            return Refused("error: window not found");

        if (!WindowProps.TryKind(property, out var kind))
            return Refused("error: invalid prop name");

        var window = client.Window;
        var key = WindowProps.StorageKey(property);
        object? next;
        switch (kind)
        {
            case PropKind.Size:
                if (!ExpressionPair.TryParse(value, out var pair)
                    || !pair!.TryEvaluate(ExpressionVariables(client, desk), out var width, out var height))
                    return Refused("error: failed to parse expression");
                next = new PropSize(width, height);
                break;
            case PropKind.Alpha:
                if (!WindowProps.TryFloat(value, out var alpha))
                    return Refused("error: invalid opacity");
                next = new PropAlpha(alpha, ((PropAlpha)PropValue(window, desk, key, kind)).Overridden);
                break;
            case PropKind.AlphaOverride:
                var flag = WindowProps.TryInteger(value, out var number) && number != 0;
                next = ((PropAlpha)PropValue(window, desk, key, PropKind.Alpha)) with { Overridden = flag };
                break;
            case PropKind.Color:
                if (!WindowProps.TryReadColor(value, out var color))
                    return View([], dispatcher: dispatcher);
                next = color;
                break;
            default:
                // A value that does not parse changes nothing.
                if (!WindowProps.TryTrivial(key, kind, value, Layered(window, desk, key), PropValue(window, desk, key, kind), out next))
                    return View([], dispatcher: dispatcher);
                break;
        }

        if (next is null)
        {
            if (_setProps.TryGetValue(window, out var props))
                props.Remove(key);
        }
        else
        {
            if (!_setProps.TryGetValue(window, out var props))
                _setProps[window] = props = new Dictionary<string, object>(StringComparer.Ordinal);
            props[key] = next;
        }

        IReadOnlyList<Placement> placements = key switch
        {
            "min_size" or "max_size" or "no_max_size" => ApplyLimits(window, desk),
            "border_size" => desk.Layout.Apply(desk.Options),
            _ => [],
        };
        return View(IsShown(desk.Id) ? placements : [], dispatcher: dispatcher);
    }

    // The variables a min_size or max_size expression reads, for this window as it is now.
    private Dictionary<string, double> ExpressionVariables(Client client, LiveWorkspace desk)
    {
        var screen = MonitorById(desk.Monitor);
        var monitor = screen?.Bounds ?? default;
        var bounds = desk.Layout.TryBounds(client.Window, out var placed) ? placed : client.Bounds;
        return Variables(monitor, bounds, _pointer ?? default);
    }

    // The size rules of a window with what setprop set on top.
    private SizeRule EffectiveSizes(WindowId window, SizeRule rule) => rule with
    {
        Min = (SetPropOf(window, "min_size") as PropSize?)?.ToLimit() ?? rule.Min,
        Max = (SetPropOf(window, "max_size") as PropSize?)?.ToLimit() ?? rule.Max,
        NoMax = SetBool(window, "no_max_size") ?? rule.NoMax,
    };

    private IReadOnlyList<Placement> ApplyLimits(WindowId window, LiveWorkspace desk)
    {
        if (!_sizes.TryGetValue(window, out var sizes))
            return [];

        var rule = EffectiveSizes(window, sizes.Rule);
        var floating = rule.Floating(sizes.OwnMin, sizes.OwnMax);
        return desk.Layout.UpdateLimits(window, floating.Min, floating.Max, rule.Tiled(sizes.OwnMin, sizes.OwnMax, _session.SizeLimitsTiled));
    }

    // The opacity a window's props ask for: setprop's fractions over the opacity rule's.
    private OpacityChoice PropOpacity(WindowId window, OpacityChoice ruled)
    {
        if (!_setProps.TryGetValue(window, out var props))
            return ruled;

        OpacityFactor Factor(string key, OpacityFactor fallback) =>
            props.TryGetValue(key, out var value) && value is PropAlpha alpha ? new OpacityFactor(alpha.Value, alpha.Overridden) : fallback;

        var rule = ruled.Rule ?? new OpacityRule(OpacityFactor.Identity, OpacityFactor.Identity, OpacityFactor.Identity);
        var merged = new OpacityRule(Factor("opacity", rule.Active), Factor("opacity_inactive", rule.Inactive), Factor("opacity_fullscreen", rule.Fullscreen));
        var opaque = props.TryGetValue("opaque", out var held) && held is bool flag ? flag : ruled.Opaque;
        return new OpacityChoice(ruled.Rule is null && merged == rule ? null : merged, opaque);
    }
}
