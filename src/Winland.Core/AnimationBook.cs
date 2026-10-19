using System.Globalization;

namespace Winland;

/// <summary>
/// The animation tree and the named curves while a config is read. Each <c>animation</c> line sets one
/// node, and a node that is not set takes the values of its nearest set ancestor.
/// </summary>
internal sealed class AnimationBook
{
    internal const string BezierKeyword = "bezier";
    internal const string AnimationKeyword = "animation";
    internal const string GapPrefix = "animation:";

    // Child, parent. The root is global.
    private static readonly Dictionary<string, string?> Parents = new(StringComparer.Ordinal)
    {
        ["global"] = null,
        ["windows"] = "global",
        ["windowsIn"] = "windows",
        ["windowsOut"] = "windows",
        ["windowsMove"] = "windows",
        ["layers"] = "global",
        ["layersIn"] = "layers",
        ["layersOut"] = "layers",
        ["fade"] = "global",
        ["fadeIn"] = "fade",
        ["fadeOut"] = "fade",
        ["fadeSwitch"] = "fade",
        ["fadeShadow"] = "fade",
        ["fadeGlow"] = "fade",
        ["fadeDim"] = "fade",
        ["fadeLayers"] = "fade",
        ["fadeLayersIn"] = "fadeLayers",
        ["fadeLayersOut"] = "fadeLayers",
        ["fadePopups"] = "fade",
        ["fadePopupsIn"] = "fadePopups",
        ["fadePopupsOut"] = "fadePopups",
        ["fadeDpms"] = "fade",
        ["border"] = "global",
        ["borderangle"] = "global",
        ["shadowangle"] = "global",
        ["glowangle"] = "global",
        ["workspaces"] = "global",
        ["workspacesIn"] = "workspaces",
        ["workspacesOut"] = "workspaces",
        ["specialWorkspace"] = "workspaces",
        ["specialWorkspaceIn"] = "specialWorkspace",
        ["specialWorkspaceOut"] = "specialWorkspace",
        ["zoomFactor"] = "global",
        ["monitorAdded"] = "global",
    };

    private readonly Dictionary<string, AnimationLeaf> _set = new(StringComparer.Ordinal)
    {
        ["global"] = new(true, 8, "default", ""),
        ["borderangle"] = new(false, 1, "default", ""),
        ["shadowangle"] = new(false, 1, "default", ""),
        ["glowangle"] = new(false, 1, "default", ""),
    };

    private readonly Dictionary<string, Bezier> _curves = new(StringComparer.Ordinal)
    {
        ["default"] = Winland.Bezier.Default,
        ["linear"] = Winland.Bezier.Linear,
    };

    // The nodes an animation line set, and the curves in the order they were added.
    private readonly HashSet<string> _overridden = new(StringComparer.Ordinal);
    private readonly List<string> _curveOrder = ["default", "linear"];

    internal static bool Exists(string node) => Parents.ContainsKey(node);

    /// <summary>
    /// The tree and the curves, as the pinned release answers <c>animations</c>. Its maps list them in no fixed order;
    /// here the nodes follow the tree and the curves come newest first.
    /// </summary>
    internal string Describe(bool json)
    {
        var curves = new List<string>(_curveOrder);
        curves.Reverse();
        var text = new System.Text.StringBuilder();
        if (!json)
        {
            text.Append("animations:\n");
            foreach (var node in Parents.Keys)
            {
                var leaf = Effective(node);
                text.Append(CultureInfo.InvariantCulture, $"\n\tname: {node}\n\t\toverridden: {(_overridden.Contains(node) ? 1 : 0)}\n\t\tbezier: {leaf.Curve}\n\t\tenabled: {(leaf.Enabled ? "true" : "false")}\n\t\tspeed: {leaf.Speed:0.00}\n\t\tstyle: {leaf.Style}\n");
            }

            text.Append("beziers:\n");
            foreach (var name in curves)
            {
                var curve = _curves[name];
                text.Append(CultureInfo.InvariantCulture, $"\n\tname: {name}\n\t\tX0: {curve.X1:0.00}\n\t\tY0: {curve.Y1:0.00}\n\t\tX1: {curve.X2:0.00}\n\t\tY1: {curve.Y2:0.00}");
            }

            return text.ToString();
        }

        var tree = Parents.Keys.Select(node =>
        {
            var leaf = Effective(node);
            return new Dictionary<string, object>
            {
                ["name"] = node,
                ["overridden"] = _overridden.Contains(node),
                ["bezier"] = leaf.Curve,
                ["enabled"] = leaf.Enabled,
                ["speed"] = Math.Round(leaf.Speed, 2),
                ["style"] = leaf.Style,
            };
        });
        var points = curves.Select(name => new Dictionary<string, object>
        {
            ["name"] = name,
            ["X0"] = Math.Round(_curves[name].X1, 2),
            ["Y0"] = Math.Round(_curves[name].Y1, 2),
            ["X1"] = Math.Round(_curves[name].X2, 2),
            ["Y1"] = Math.Round(_curves[name].Y2, 2),
        });
        return System.Text.Json.JsonSerializer.Serialize(new object[] { tree.ToArray(), points.ToArray() });
    }

    internal bool TryCurve(string name, out Bezier curve) => _curves.TryGetValue(name, out curve!);

    internal AnimationLeaf Effective(string node)
    {
        string? current = node;
        while (current is not null)
        {
            if (_set.TryGetValue(current, out var leaf))
                return leaf;

            current = Parents.GetValueOrDefault(current);
        }

        return _set["global"];
    }

    /// <summary>Reads <c>NAME, X0, Y0, X1, Y1</c>. Returns the warning, or null when the curve is kept.</summary>
    internal string? AddBezier(string value)
    {
        var args = Split(value);
        var points = new double[4];
        for (var i = 0; i < 4; i++)
        {
            var text = Arg(args, i + 1);
            if (text.Length == 0)
                return "Bezier needs four points.";

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out points[i]) || !double.IsFinite(points[i]))
                return $"Bezier point '{text}' is not a number.";
        }

        if (Arg(args, 5).Length > 0)
            return "Bezier has too many arguments.";

        var curveName = Arg(args, 0);
        if (!_curves.ContainsKey(curveName))
            _curveOrder.Add(curveName);
        _curves[curveName] = new Winland.Bezier(points[0], points[1], points[2], points[3]);
        return null;
    }

    /// <summary>Reads <c>NAME, ONOFF, SPEED, CURVE[, STYLE]</c>. Returns the warning, or null when the line is kept.</summary>
    internal string? SetAnimation(string value)
    {
        var args = Split(value);
        var name = Arg(args, 0);
        if (!Exists(name))
            return $"Animation '{name}' does not exist.";

        if (ConfigGaps.ReasonFor(GapPrefix + name) is string reason)
            return reason;

        _overridden.Add(name);
        if (!IsOn(Arg(args, 1)))
        {
            _set[name] = new AnimationLeaf(false, 1, "default", "");
            return null;
        }

        var speedText = Arg(args, 2);
        if (!double.TryParse(speedText, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) || !double.IsFinite(speed) || speed <= 0)
            return $"Animation speed '{speedText}' is not valid.";

        var curve = Arg(args, 3);
        if (!_curves.ContainsKey(curve))
            return $"Curve '{curve}' does not exist.";

        // The style is kept even when it is not valid, and only warns.
        var style = Arg(args, 4);
        _set[name] = new AnimationLeaf(true, speed, curve, style);
        return style.Length == 0 ? null : CheckStyle(name, style);
    }

    private static string? CheckStyle(string name, string style)
    {
        var bad = $"Style '{style}' is not valid for animation '{name}'.";
        if (name.StartsWith("window", StringComparison.Ordinal))
        {
            if (style.StartsWith("slide", StringComparison.Ordinal) || style is "gnome" or "gnomed")
                return null;

            if (style.StartsWith("popin", StringComparison.Ordinal))
                return !style.Contains('%', StringComparison.Ordinal) || TryLastPercent(style, wholeWhenNoSpace: false, out _) ? null : bad;

            return bad;
        }

        if (name.StartsWith("workspaces", StringComparison.Ordinal) || name.StartsWith("specialWorkspace", StringComparison.Ordinal))
        {
            if (style is "slide" or "slidevert" or "fade")
                return null;

            if (style.StartsWith("slide", StringComparison.Ordinal))
                return !style.Contains('%', StringComparison.Ordinal) || TryLastPercent(style, wholeWhenNoSpace: true, out _) ? null : bad;

            return bad;
        }

        if (name == "borderangle")
            return style is "once" or "loop" ? null : bad;

        return $"Animation '{name}' has no styles.";
    }

    /// <summary>
    /// Reads the percentage in the last word of a style, as in <c>popin 80%</c>. The trailing character is
    /// dropped and the leading digits are read.
    /// </summary>
    internal static bool TryLastPercent(string style, bool wholeWhenNoSpace, out int percent)
    {
        percent = 0;
        var space = style.LastIndexOf(' ');
        if (space < 0 && !wholeWhenNoSpace)
            return false;

        var word = style[(space + 1)..];
        if (word.Length == 0)
            return false;

        var digits = word[..^1].TrimStart();
        var length = 0;
        if (length < digits.Length && digits[length] is '+' or '-')
            length++;

        var start = length;
        while (length < digits.Length && char.IsAsciiDigit(digits[length]))
            length++;

        return length > start && int.TryParse(digits[..length], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out percent);
    }

    private static bool IsOn(string text)
    {
        if (ConfigValueSyntax.TryParseBool(text, out var on))
            return on;

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number == 1;
    }

    private static string[] Split(string value)
    {
        var parts = value.Split(',');
        for (var i = 0; i < parts.Length; i++)
            parts[i] = parts[i].Trim();
        return parts;
    }

    private static string Arg(string[] args, int index) => index < args.Length ? args[index] : "";
}
