using System.Globalization;

namespace Winland;

public enum MasterOrientation
{
    Left,
    Top,
    Right,
    Bottom,
    Center,
}

public enum MasterNewStatus
{
    Slave,
    Master,
    Inherit,
}

public enum MasterNewOnActive
{
    None,
    Before,
    After,
}

/// <summary>Options of the <c>master:</c> category. Defaults match the pinned layout source.</summary>
public sealed record MasterOptions
{
    public bool AllowSmallSplit { get; init; }

    public double Mfact { get; init; } = 0.55;

    public MasterNewStatus NewStatus { get; init; } = MasterNewStatus.Slave;

    public bool NewOnTop { get; init; }

    public MasterNewOnActive NewOnActive { get; init; } = MasterNewOnActive.None;

    public MasterOrientation Orientation { get; init; } = MasterOrientation.Left;

    public int SlaveCountForCenter { get; init; } = 2;

    public MasterOrientation CenterFallback { get; init; } = MasterOrientation.Left;

    public bool SmartResizing { get; init; } = true;

    public bool DropAtCursor { get; init; } = true;

    public bool AlwaysKeepPosition { get; init; }

    public bool FocusMasterOnClose { get; init; }

    public static MasterOptions Documented { get; } = new();

    private static readonly Dictionary<string, Func<MasterOptions, string, MasterOptions>> Appliers = new(StringComparer.Ordinal)
    {
        ["master:allow_small_split"] = (options, value) => TryBool(value, out var allow) ? options with { AllowSmallSplit = allow } : options,
        ["master:mfact"] = (options, value) => TryDouble(value, out var mfact) ? options with { Mfact = Clamp(mfact, 0, 1) } : options,
        ["master:new_status"] = (options, value) => TryStatus(value, out var status) ? options with { NewStatus = status } : options,
        ["master:new_on_top"] = (options, value) => TryBool(value, out var top) ? options with { NewOnTop = top } : options,
        ["master:new_on_active"] = (options, value) => TryOnActive(value, out var placed) ? options with { NewOnActive = placed } : options,
        ["master:orientation"] = (options, value) => TryOrientation(value, out var orientation) ? options with { Orientation = orientation } : options,
        ["master:slave_count_for_center_master"] = (options, value) => TryInt(value, out var count) ? options with { SlaveCountForCenter = Clamp(count, 0, 10) } : options,
        ["master:center_master_fallback"] = (options, value) => TryFallback(value, out var fallback) ? options with { CenterFallback = fallback } : options,
        ["master:smart_resizing"] = (options, value) => TryBool(value, out var smart) ? options with { SmartResizing = smart } : options,
        ["master:drop_at_cursor"] = (options, value) => TryBool(value, out var drop) ? options with { DropAtCursor = drop } : options,
        ["master:always_keep_position"] = (options, value) => TryBool(value, out var keep) ? options with { AlwaysKeepPosition = keep } : options,
        ["master:focus_master_on_close"] = (options, value) => TryBool(value, out var focus) ? options with { FocusMasterOnClose = focus } : options,
    };

    internal static IReadOnlySet<string> Keywords { get; } = Appliers.Keys.ToHashSet(StringComparer.Ordinal);

    public static MasterOptions From(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = Documented;
        foreach (var setting in document.Settings)
            options = Apply(options, setting);
        return options;
    }

    private static MasterOptions Apply(MasterOptions options, Setting setting) =>
        Appliers.TryGetValue(setting.Keyword, out var apply) ? apply(options, setting.Value) : options;

    private static bool TryStatus(string text, out MasterNewStatus status)
    {
        if (Is(text, "master"))
            status = MasterNewStatus.Master;
        else if (Is(text, "slave"))
            status = MasterNewStatus.Slave;
        else if (Is(text, "inherit"))
            status = MasterNewStatus.Inherit;
        else
        {
            status = MasterNewStatus.Slave;
            return false;
        }

        return true;
    }

    private static bool TryOnActive(string text, out MasterNewOnActive placed)
    {
        if (Is(text, "before"))
            placed = MasterNewOnActive.Before;
        else if (Is(text, "after"))
            placed = MasterNewOnActive.After;
        else if (Is(text, "none"))
            placed = MasterNewOnActive.None;
        else
        {
            placed = MasterNewOnActive.None;
            return false;
        }

        return true;
    }

    private static bool TryOrientation(string text, out MasterOrientation orientation)
    {
        if (Is(text, "top"))
            orientation = MasterOrientation.Top;
        else if (Is(text, "right"))
            orientation = MasterOrientation.Right;
        else if (Is(text, "bottom"))
            orientation = MasterOrientation.Bottom;
        else if (Is(text, "center"))
            orientation = MasterOrientation.Center;
        else if (Is(text, "left"))
            orientation = MasterOrientation.Left;
        else if (text.Length > 0)
            orientation = MasterOrientation.Left;
        else
        {
            orientation = MasterOrientation.Left;
            return false;
        }

        return true;
    }

    private static bool TryFallback(string text, out MasterOrientation orientation)
    {
        if (Is(text, "right"))
            orientation = MasterOrientation.Right;
        else if (Is(text, "top"))
            orientation = MasterOrientation.Top;
        else if (Is(text, "bottom"))
            orientation = MasterOrientation.Bottom;
        else if (Is(text, "left") || text.Length > 0)
            orientation = MasterOrientation.Left;
        else
        {
            orientation = MasterOrientation.Left;
            return false;
        }

        return true;
    }

    private static bool TryBool(string text, out bool value) =>
        ConfigValueSyntax.TryParseBool(text, out value);

    private static bool TryInt(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool Is(string text, string expected) =>
        text.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);
}
