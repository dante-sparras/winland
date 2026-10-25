namespace Winland.Application;

/// <summary>
/// Window properties, as Winland's <c>getprop</c> and <c>setprop</c> reach them. A property's value is what
/// <c>setprop</c> set, else what a window rule gives, else the config's value.
/// </summary>
public sealed partial class WindowManager
{
    // The border a window's own properties give it, which the Layouts read through LayoutOptions.ClientBorder.
    private int? ClientBorderOf(ClientId client) =>
        _props.Own(new WindowId(client.Value), WindowProp.BorderSize) is { } border ? (int)Math.Clamp(border, 0, int.MaxValue) : null;

    private object DefaultProp(WindowId window, string key, PropKind kind) => key switch
    {
        "animation" => "",
        "max_size" => PropSize.Unbounded,
        "min_size" => new PropSize(20, 20),
        "opacity" or "opacity_inactive" => PropAlpha.Identity,
        "active_border_color" => BorderOf(window, active: true, honorProps: false),
        "inactive_border_color" => BorderOf(window, active: false, honorProps: false),
        "decorate" => true,
        "border_size" => (long)_config.Layout.BorderSize,
        "rounding" => (long)_config.Decoration.Rounding,
        _ when kind == PropKind.Bool => false,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No default for this property."),
    };

    private object PropValue(WindowId window, string key, PropKind kind) =>
        _props.Value(window, key) ?? DefaultProp(window, key, kind);

    private CommandReply ReadProp(GetPropRequest request)
    {
        if (request.Selector.Length == 0 || request.Property.Length == 0)
            return new CommandReply("not enough args", View([]));

        if (!TrySelect(request.Selector, out var client, out _))
            return new CommandReply("window not found", View([]));

        if (!WindowProps.TryKind(request.Property, out var kind))
            return new CommandReply("prop not found", View([]));

        var key = request.Property;
        var value = PropValue(client.Window, key, kind);
        var json = request.Flags.HasFlag(CommandFlags.Json);
        var text = WindowProps.Format(key, kind, value, json, animationSet: _props.SetProp(client.Window, key) is not null);
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
        var key = property;
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
                next = new PropAlpha(alpha);
                break;
            case PropKind.Color:
                if (!WindowProps.TryReadColor(value, out var color))
                    return View([], dispatcher: dispatcher);
                next = color;
                break;
            default:
                // A value that does not parse changes nothing.
                if (!WindowProps.TryTrivial(key, kind, value, _props.Value(window, key), PropValue(window, key, kind), out next))
                    return View([], dispatcher: dispatcher);
                break;
        }

        _props.Set(window, key, next);

        IReadOnlyList<Placement> placements = key switch
        {
            "min_size" or "max_size" => ApplyLimits(window, desk),
            "border_size" => desk.Layout.Apply(desk.Options),
            _ => [],
        };
        return View(_workspaces.Shown.IsShown(desk.Id) ? placements : [], dispatcher: dispatcher);
    }

    // The variables a min_size or max_size expression reads, for this window as it is now.
    private Dictionary<string, double> ExpressionVariables(Client client, LiveWorkspace desk)
    {
        var screen = MonitorById(desk.Monitor);
        var monitor = screen?.Bounds ?? default;
        var bounds = desk.Layout.TryBounds(client.Window, out var placed) ? placed : client.Bounds;
        return Variables(monitor, bounds, _pointer ?? default);
    }

    private IReadOnlyList<Placement> ApplyLimits(WindowId window, LiveWorkspace desk)
    {
        if (_props.Sizes(window) is not { } sizes)
            return [];

        var rule = _props.EffectiveSizes(window, sizes.Rule);
        var floating = rule.Floating(sizes.OwnMin, sizes.OwnMax);
        return desk.Layout.UpdateLimits(window, floating.Min, floating.Max, new TiledLimits(sizes.OwnMin, sizes.OwnMax));
    }
}
