using System.Globalization;
using System.Text.Json;

namespace Winland;

internal static partial class OptionCatalog
{
    private static readonly ConfigDocument Empty = new([], [], []);

    // Every option getoption and descriptions answer, in the order descriptions lists them.
    private static readonly string[] Names =
    [
        "general:gaps_in",
        "general:gaps_out",
        "general:border_size",
        "general:resize_on_border",
        "general:extend_border_grab_area",
        "general:hover_icon_on_border",
        "general:resize_corner",
        "general:float_gaps",
        "general:snap:enabled",
        "general:snap:window_gap",
        "general:snap:monitor_gap",
        "general:snap:border_overlap",
        "general:snap:respect_gaps",
        "general:layout",
        "general:col.active_border",
        "general:col.inactive_border",
        "decoration:rounding",
        "decoration:rounding_power",
        "decoration:blur:enabled",
        "decoration:shadow:enabled",
        "master:new_status",
        "dwindle:force_split",
        "dwindle:preserve_split",
        "dwindle:smart_split",
        "dwindle:permanent_direction_override",
        "dwindle:split_width_multiplier",
        "dwindle:use_active_for_splits",
        "dwindle:default_split_ratio",
        "dwindle:split_bias",
        "dwindle:smart_resizing",
        "dwindle:precise_mouse_move",
        "binds:drag_threshold",
        "binds:scroll_event_delay",
        "binds:workspace_back_and_forth",
        "binds:pass_mouse_when_bound",
        "binds:workspace_center_on",
        "binds:drag_center_window",
        "binds:allow_workspace_cycles",
        "binds:focus_preferred_method",
        "binds:window_direction_monitor_fallback",
        "layout:single_window_aspect_ratio",
        "layout:single_window_aspect_ratio_tolerance",
        "misc:float_force_onscreen",
        "misc:new_float_force_onscreen",
        "misc:initial_workspace_tracking",
        "misc:initial_workspace_token_timeout",
        "misc:disable_autoreload",
        "misc:size_limits_tiled",
        "misc:focus_on_activate",
        "general:modal_parent_blocking",
        "misc:close_special_on_empty",
        "binds:hide_special_on_workspace_change",
        "input:special_fallthrough",
        "scrolling:fullscreen_on_one_column",
        "scrolling:column_width",
        "scrolling:focus_fit_method",
        "scrolling:follow_focus",
        "scrolling:follow_min_visible",
        "scrolling:explicit_column_widths",
        "scrolling:direction",
        "scrolling:wrap_focus",
        "scrolling:wrap_swapcol",
    ];

    /// <summary>
    /// Each option with its default and its current value, written as JSON, as the pinned release answers
    /// <c>descriptions</c>, with its description. Only the options Winland reads are listed.
    /// </summary>
    public static string Descriptions(ConfigDocument? config)
    {
        var entries = new List<string>(Names.Length);
        foreach (var name in Names)
        {
            if (!TryValue(name, Empty, out _, out var fallback) || !TryValue(name, config ?? Empty, out _, out var current))
                continue;

            entries.Add("{\"name\":" + JsonSerializer.Serialize(name) + ",\"description\":" + JsonSerializer.Serialize(DescriptionOf.GetValueOrDefault(name, "")) + ",\"default\":" + fallback + ",\"current\":" + current + "}");
        }

        return "[" + string.Join(',', entries) + "]";
    }

    public static string Describe(string name, ConfigDocument? config)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!TryValue(name, config ?? Empty, out var kind, out var token))
            return "no such option";

        var set = false;
        if (config is not null)
        {
            foreach (var setting in config.Settings)
            {
                if (setting.Keyword == name)
                    set = true;
            }
        }

        var option = JsonSerializer.Serialize(name);
        return "{\"option\":" + option + ",\"" + kind + "\":" + token + ",\"set\":" + (set ? "true" : "false") + "}";
    }

    private static bool TryValue(string name, ConfigDocument document, out string kind, out string token)
    {
        var layout = LayoutOptions.From(document);
        var decoration = DecorationOptions.From(document);
        var scrolling = ScrollingOptions.From(document);
        var session = SessionOptions.From(document);
        var input = InputOptions.From(document);
        var master = MasterOptions.From(document);
        switch (name)
        {
            case "master:new_status":
                kind = "str";
                token = JsonSerializer.Serialize(master.NewStatus.ToString().ToLowerInvariant());
                return true;
            case "general:gaps_in":
                kind = "css";
                token = JsonSerializer.Serialize(Gaps(layout.GapsIn));
                return true;
            case "general:gaps_out":
                kind = "css";
                token = JsonSerializer.Serialize(Gaps(layout.GapsOut));
                return true;
            case "general:border_size":
                kind = "int";
                token = layout.BorderSize.ToString(CultureInfo.InvariantCulture);
                return true;
            case "general:resize_on_border":
                kind = "bool";
                token = Bool(layout.ResizeOnBorder);
                return true;
            case "general:extend_border_grab_area":
                kind = "int";
                token = layout.ExtendBorderGrabArea.ToString(CultureInfo.InvariantCulture);
                return true;
            case "general:hover_icon_on_border":
                kind = "bool";
                token = Bool(layout.HoverIconOnBorder);
                return true;
            case "general:resize_corner":
                kind = "int";
                token = layout.ResizeCorner.ToString(CultureInfo.InvariantCulture);
                return true;
            case "general:float_gaps":
                kind = "css";
                token = JsonSerializer.Serialize(Gaps(layout.FloatGaps));
                return true;
            case "general:snap:enabled":
                kind = "bool";
                token = Bool(layout.SnapEnabled);
                return true;
            case "general:snap:window_gap":
                kind = "int";
                token = layout.SnapWindowGap.ToString(CultureInfo.InvariantCulture);
                return true;
            case "general:snap:monitor_gap":
                kind = "int";
                token = layout.SnapMonitorGap.ToString(CultureInfo.InvariantCulture);
                return true;
            case "general:snap:border_overlap":
                kind = "bool";
                token = Bool(layout.SnapBorderOverlap);
                return true;
            case "general:snap:respect_gaps":
                kind = "bool";
                token = Bool(layout.SnapRespectGaps);
                return true;
            case "general:layout":
                kind = "str";
                token = JsonSerializer.Serialize(LayoutName(document));
                return true;
            case "general:col.active_border":
                kind = "str";
                token = JsonSerializer.Serialize(Hex(decoration.ActiveBorder));
                return true;
            case "general:col.inactive_border":
                kind = "str";
                token = JsonSerializer.Serialize(Hex(decoration.InactiveBorder));
                return true;
            case "decoration:rounding":
                kind = "int";
                token = decoration.Rounding.ToString(CultureInfo.InvariantCulture);
                return true;
            case "decoration:rounding_power":
                kind = "float";
                token = JsonSerializer.Serialize(decoration.RoundingPower);
                return true;
            case "decoration:blur:enabled":
                kind = "bool";
                token = Bool(decoration.Blur);
                return true;
            case "decoration:shadow:enabled":
                kind = "bool";
                token = Bool(decoration.Shadow);
                return true;
            case "dwindle:force_split":
                kind = "int";
                token = layout.ForceSplit.ToString(CultureInfo.InvariantCulture);
                return true;
            case "dwindle:preserve_split":
                kind = "bool";
                token = Bool(layout.PreserveSplit);
                return true;
            case "dwindle:smart_split":
                kind = "bool";
                token = Bool(layout.SmartSplit);
                return true;
            case "dwindle:permanent_direction_override":
                kind = "bool";
                token = Bool(layout.PermanentDirectionOverride);
                return true;
            case "dwindle:split_width_multiplier":
                kind = "float";
                token = JsonSerializer.Serialize(layout.SplitWidthMultiplier);
                return true;
            case "dwindle:use_active_for_splits":
                kind = "bool";
                token = Bool(layout.UseActiveForSplits);
                return true;
            case "dwindle:default_split_ratio":
                kind = "float";
                token = JsonSerializer.Serialize(layout.DefaultSplitRatio);
                return true;
            case "dwindle:split_bias":
                kind = "int";
                token = layout.SplitBias.ToString(CultureInfo.InvariantCulture);
                return true;
            case "dwindle:smart_resizing":
                kind = "bool";
                token = Bool(layout.SmartResizing);
                return true;
            case "dwindle:precise_mouse_move":
                kind = "bool";
                token = Bool(layout.PreciseMouseMove);
                return true;
            case "binds:drag_threshold":
                kind = "int";
                token = LastInt(document, name, 0, 0, int.MaxValue).ToString(CultureInfo.InvariantCulture);
                return true;
            case "binds:scroll_event_delay":
                kind = "int";
                token = LastInt(document, name, 300, 0, 2000).ToString(CultureInfo.InvariantCulture);
                return true;
            case "binds:workspace_back_and_forth":
                kind = "int";
                token = BackAndForth(document).ToString(CultureInfo.InvariantCulture);
                return true;
            case "binds:pass_mouse_when_bound":
                kind = "bool";
                token = Bool(LastBool(document, name, false));
                return true;
            case "binds:workspace_center_on":
                kind = "int";
                token = LastInt(document, name, 1, 0, 1).ToString(CultureInfo.InvariantCulture);
                return true;
            case "binds:drag_center_window":
                kind = "bool";
                token = Bool(LastBool(document, name, true));
                return true;
            case "binds:allow_workspace_cycles":
                kind = "bool";
                token = Bool(LastBool(document, name, false));
                return true;
            case "binds:focus_preferred_method":
                kind = "int";
                token = LastInt(document, name, 0, 0, 1).ToString(CultureInfo.InvariantCulture);
                return true;
            case "binds:window_direction_monitor_fallback":
                kind = "bool";
                token = Bool(MonitorFallback(document));
                return true;
            case "layout:single_window_aspect_ratio":
                kind = "vec2";
                token = JsonSerializer.Serialize(new[] { layout.SingleWindowAspectRatio.X, layout.SingleWindowAspectRatio.Y });
                return true;
            case "layout:single_window_aspect_ratio_tolerance":
                kind = "float";
                token = layout.SingleWindowAspectRatioTolerance.ToString(CultureInfo.InvariantCulture);
                return true;
            case "misc:float_force_onscreen":
                kind = "int";
                token = layout.FloatForceOnscreen.ToString(CultureInfo.InvariantCulture);
                return true;
            case "misc:new_float_force_onscreen":
                kind = "int";
                token = layout.NewFloatForceOnscreen.ToString(CultureInfo.InvariantCulture);
                return true;
            case "misc:initial_workspace_tracking":
                kind = "int";
                token = session.InitialWorkspaceTracking.ToString(CultureInfo.InvariantCulture);
                return true;
            case "misc:initial_workspace_token_timeout":
                kind = "int";
                token = session.InitialWorkspaceTokenTimeout.ToString(CultureInfo.InvariantCulture);
                return true;
            case "misc:disable_autoreload":
                kind = "bool";
                token = Bool(session.DisableAutoreload);
                return true;
            case "misc:size_limits_tiled":
                kind = "bool";
                token = Bool(session.SizeLimitsTiled);
                return true;
            case "misc:focus_on_activate":
                kind = "bool";
                token = Bool(session.FocusOnActivate);
                return true;
            case "general:modal_parent_blocking":
                kind = "bool";
                token = Bool(session.ModalParentBlocking);
                return true;
            case "misc:close_special_on_empty":
                kind = "bool";
                token = Bool(session.CloseSpecialOnEmpty);
                return true;
            case "binds:hide_special_on_workspace_change":
                kind = "bool";
                token = Bool(session.HideSpecialOnWorkspaceChange);
                return true;
            case "input:special_fallthrough":
                kind = "bool";
                token = Bool(input.SpecialFallthrough);
                return true;
            case "scrolling:fullscreen_on_one_column":
                kind = "bool";
                token = Bool(scrolling.FullscreenOnOneColumn);
                return true;
            case "scrolling:column_width":
                kind = "float";
                token = JsonSerializer.Serialize(scrolling.ColumnWidth);
                return true;
            case "scrolling:focus_fit_method":
                kind = "int";
                token = ((int)scrolling.FocusFitMethod).ToString(CultureInfo.InvariantCulture);
                return true;
            case "scrolling:follow_focus":
                kind = "bool";
                token = Bool(scrolling.FollowFocus);
                return true;
            case "scrolling:follow_min_visible":
                kind = "float";
                token = JsonSerializer.Serialize(scrolling.FollowMinVisible);
                return true;
            case "scrolling:explicit_column_widths":
                kind = "str";
                token = JsonSerializer.Serialize(Widths(scrolling.ExplicitColumnWidths));
                return true;
            case "scrolling:direction":
                kind = "str";
                token = JsonSerializer.Serialize(Direction(scrolling.Direction));
                return true;
            case "scrolling:wrap_focus":
                kind = "bool";
                token = Bool(scrolling.WrapFocus);
                return true;
            case "scrolling:wrap_swapcol":
                kind = "bool";
                token = Bool(scrolling.WrapSwapCol);
                return true;
            default:
                kind = "";
                token = "";
                return false;
        }
    }

    private static string LayoutName(ConfigDocument document)
    {
        var name = "dwindle";
        foreach (var setting in document.Settings)
        {
            if (setting.Keyword == "general:layout" && setting.Value.Length > 0)
                name = setting.Value;
        }

        return name;
    }

    private static bool LastBool(ConfigDocument document, string keyword, bool fallback)
    {
        var value = fallback;
        foreach (var setting in document.Settings)
        {
            if (setting.Keyword == keyword && ConfigValueSyntax.TryParseBool(setting.Value, out var parsed))
                value = parsed;
        }

        return value;
    }

    private static int LastInt(ConfigDocument document, string keyword, int fallback, int min, int max)
    {
        var value = fallback;
        foreach (var setting in document.Settings)
        {
            if (setting.Keyword != keyword)
                continue;

            if (int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                value = Math.Clamp(parsed, min, max);
        }

        return value;
    }

    private static string Gaps(EdgeInsets gaps) =>
        string.Create(CultureInfo.InvariantCulture, $"{gaps.Top} {gaps.Right} {gaps.Bottom} {gaps.Left}");

    private static string Hex(BorderColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    private static string Widths(IReadOnlyList<double> widths)
    {
        var parts = new string[widths.Count];
        for (var i = 0; i < widths.Count; i++)
            parts[i] = widths[i].ToString("0.###", CultureInfo.InvariantCulture);

        return string.Join(' ', parts);
    }

    private static string Direction(ScrollingDirection direction) => direction switch
    {
        ScrollingDirection.Left => "left",
        ScrollingDirection.Down => "down",
        ScrollingDirection.Up => "up",
        _ => "right",
    };

    private static string Bool(bool value) => value ? "true" : "false";

    private static int BackAndForth(ConfigDocument document)
    {
        var mode = 0;
        foreach (var setting in document.Settings)
        {
            if (setting.Keyword == "binds:workspace_back_and_forth" && Core.TryBackAndForth(setting.Value, out var parsed))
                mode = parsed;
        }

        return mode;
    }

    private static bool MonitorFallback(ConfigDocument document)
    {
        var enabled = true;
        foreach (var setting in document.Settings)
        {
            if (setting.Keyword == "binds:window_direction_monitor_fallback"
                && ConfigValueSyntax.TryParseBool(setting.Value, out var parsed))
                enabled = parsed;
        }

        return enabled;
    }
}
