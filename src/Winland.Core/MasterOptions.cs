namespace Winland.Core;

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

    /// <summary><c>master:center_ignores_reserved</c>: a centered master is centered on the Monitor, not on its work area.</summary>
    public bool CenterIgnoresReserved { get; init; }

    public bool SmartResizing { get; init; } = true;

    public bool DropAtCursor { get; init; } = true;

    public bool AlwaysKeepPosition { get; init; }

    public bool FocusMasterOnClose { get; init; }

    /// <summary><c>master:special_scale_factor</c>: read and kept. A special workspace is not scaled on Windows.</summary>
    public double SpecialScaleFactor { get; init; } = 1;

    public static MasterOptions Documented { get; } = new();

    private static readonly Dictionary<string, MasterNewStatus> StatusNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["slave"] = MasterNewStatus.Slave,
        ["master"] = MasterNewStatus.Master,
        ["inherit"] = MasterNewStatus.Inherit,
    };

    private static readonly Dictionary<string, MasterNewOnActive> OnActiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = MasterNewOnActive.None,
        ["before"] = MasterNewOnActive.Before,
        ["after"] = MasterNewOnActive.After,
    };

    private static readonly Dictionary<string, MasterOrientation> OrientationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = MasterOrientation.Left,
        ["top"] = MasterOrientation.Top,
        ["right"] = MasterOrientation.Right,
        ["bottom"] = MasterOrientation.Bottom,
        ["center"] = MasterOrientation.Center,
    };

    private static readonly Dictionary<string, MasterOrientation> FallbackNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = MasterOrientation.Left,
        ["top"] = MasterOrientation.Top,
        ["right"] = MasterOrientation.Right,
        ["bottom"] = MasterOrientation.Bottom,
    };

    internal static OptionSet<MasterOptions> Options { get; } = new(
    [
        Of("master:allow_small_split", OptionValue.Bool, o => o.AllowSmallSplit, (o, v) => o with { AllowSmallSplit = v }),
        Of("master:special_scale_factor", OptionValue.Number(0, 1), o => o.SpecialScaleFactor, (o, v) => o with { SpecialScaleFactor = v }),
        Of("master:mfact", OptionValue.Number(0, 1), o => o.Mfact, (o, v) => o with { Mfact = v }),
        Of("master:new_status", OptionValue.Named(StatusNames), o => o.NewStatus, (o, v) => o with { NewStatus = v }),
        Of("master:new_on_top", OptionValue.Bool, o => o.NewOnTop, (o, v) => o with { NewOnTop = v }),
        Of("master:new_on_active", OptionValue.Named(OnActiveNames), o => o.NewOnActive, (o, v) => o with { NewOnActive = v }),
        Of("master:orientation", OptionValue.Named(OrientationNames), o => o.Orientation, (o, v) => o with { Orientation = v }),
        Of("master:slave_count_for_center_master", OptionValue.Int(0, 10), o => o.SlaveCountForCenter, (o, v) => o with { SlaveCountForCenter = v }),
        Of("master:center_ignores_reserved", OptionValue.Bool, o => o.CenterIgnoresReserved, (o, v) => o with { CenterIgnoresReserved = v }),
        Of("master:center_master_fallback", OptionValue.Named(FallbackNames), o => o.CenterFallback, (o, v) => o with { CenterFallback = v }),
        Of("master:smart_resizing", OptionValue.Bool, o => o.SmartResizing, (o, v) => o with { SmartResizing = v }),
        Of("master:drop_at_cursor", OptionValue.Bool, o => o.DropAtCursor, (o, v) => o with { DropAtCursor = v }),
        Of("master:always_keep_position", OptionValue.Bool, o => o.AlwaysKeepPosition, (o, v) => o with { AlwaysKeepPosition = v }),
        Of("master:focus_master_on_close", OptionValue.Bool, o => o.FocusMasterOnClose, (o, v) => o with { FocusMasterOnClose = v }),
    ]);

    public static MasterOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<MasterOptions> Of<T>(string name, OptionValue<T> value, Func<MasterOptions, T> get, Func<MasterOptions, T, MasterOptions> set) =>
        Option<MasterOptions>.Of(name, value, get, set);
}
