namespace Winland.Domain;

/// <summary>
/// The config in force: the document read last and every option it sets, read in one go. A reload replaces the whole
/// record, so a reader that takes it once sees one config, never part of two.
/// </summary>
public sealed record AppliedConfig
{
    /// <summary>The options before any config is read. Layouts keep their own defaults until one is.</summary>
    public static AppliedConfig Unconfigured { get; } = new();

    /// <summary>The document read last, or null before the first.</summary>
    public ConfigDocument? Document { get; init; }

    public IReadOnlyList<WindowRule> WindowRules => Document?.WindowRules ?? [];

    public IReadOnlyList<WorkspaceRule> WorkspaceRules => Document?.WorkspaceRules ?? WorkspaceRule.None;

    public IReadOnlyList<TrackpadGesture> Gestures => Document?.Gestures ?? [];

    public BindTable Binds { get; init; } = BindTable.Empty;

    public BindOptions BindOptions { get; init; } = BindOptions.Documented;

    public LayoutOptions Layout { get; init; } = LayoutOptions.Unconfigured;

    public ScrollingOptions Scrolling { get; init; } = ScrollingOptions.Defaults;

    public MasterOptions Master { get; init; } = MasterOptions.Documented;

    /// <summary>The decoration options, with the system accent color filled in where the config asks for it.</summary>
    public DecorationOptions Decoration { get; init; } = DecorationOptions.Documented;

    public GroupOptions Groups { get; init; } = GroupOptions.Documented;

    public SessionOptions Session { get; init; } = SessionOptions.Documented;

    public DebugOptions Debug { get; init; } = DebugOptions.Documented;

    public AnimationOptions Animations { get; init; } = AnimationOptions.Documented;

    public CursorOptions Cursor { get; init; } = CursorOptions.Documented;

    public InputOptions Input { get; init; } = InputOptions.Documented;

    public GestureOptions Swipe { get; init; } = GestureOptions.Documented;

    /// <summary>Reads every option <paramref name="document"/> sets. <paramref name="accent"/> is the system accent color.</summary>
    public static AppliedConfig From(ConfigDocument document, BorderColor? accent)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new AppliedConfig
        {
            Document = document,
            Binds = BindTable.From(document),
            BindOptions = BindOptions.From(document),
            Layout = LayoutOptions.From(document),
            Scrolling = ScrollingOptions.From(document),
            Master = MasterOptions.From(document),
            Decoration = DecorationOptions.From(document).WithAccent(accent),
            Groups = GroupOptions.From(document),
            Session = SessionOptions.From(document),
            Debug = DebugOptions.From(document),
            Animations = AnimationOptions.From(document),
            Cursor = CursorOptions.From(document),
            Input = InputOptions.From(document),
            Swipe = GestureOptions.From(document),
        };
    }
}
