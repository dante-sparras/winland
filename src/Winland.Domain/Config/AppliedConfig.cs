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

    /// <summary>Whether a window rule matches on <c>title</c>. <c>initial_title</c> cannot change, so it does not count.</summary>
    public bool TitleRules { get; init; }

    public IReadOnlyList<WorkspaceRule> WorkspaceRules => Document?.WorkspaceRules ?? WorkspaceRule.None;

    public IReadOnlyList<TrackpadGesture> Gestures => Document?.Gestures ?? [];

    public BindTable Binds { get; init; } = BindTable.Empty;

    public BindOptions BindOptions { get; init; } = BindOptions.Documented;

    public LayoutOptions Layout { get; init; } = LayoutOptions.Unconfigured;

    public ScrollingOptions Scrolling { get; init; } = ScrollingOptions.Defaults;


    /// <summary>The decoration options, with the system accent color filled in where the config asks for it.</summary>
    public DecorationOptions Decoration { get; init; } = DecorationOptions.Documented;

    public GroupOptions Groups { get; init; } = GroupOptions.Documented;

    public SessionOptions Session { get; init; } = SessionOptions.Documented;

    public DebugOptions Debug { get; init; } = DebugOptions.Documented;

    public AnimationOptions Animations { get; init; } = AnimationOptions.Documented;

    public CursorOptions Cursor { get; init; } = CursorOptions.Documented;

    public InputOptions Input { get; init; } = InputOptions.Documented;

    public GestureOptions Swipe { get; init; } = GestureOptions.Documented;

    /// <summary>
    /// Reads every option <paramref name="document"/> sets. <paramref name="accent"/> is the system accent color, and
    /// <paramref name="contrast"/> the High Contrast colors while a High Contrast theme is on.
    /// </summary>
    public static AppliedConfig From(ConfigDocument document, BorderColor? accent, HighContrast? contrast = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new AppliedConfig
        {
            Document = document,
            TitleRules = document.WindowRules.Any(rule => rule.TitlePattern is not null),
            Binds = BindTable.From(document),
            BindOptions = BindOptions.From(document),
            Layout = LayoutOptions.From(document),
            Scrolling = ScrollingOptions.From(document),
            Decoration = Decorated(document, accent, contrast),
            Groups = GroupOptions.From(document, contrast),
            Session = SessionOptions.From(document),
            Debug = DebugOptions.From(document),
            Animations = AnimationOptions.From(document),
            Cursor = CursorOptions.From(document),
            Input = InputOptions.From(document),
            Swipe = GestureOptions.From(document),
        };
    }

    /// <summary>This config under another accent and High Contrast state. Only the colors they stand in for change.</summary>
    public AppliedConfig WithSystemColors(BorderColor? accent, HighContrast? contrast)
    {
        var document = Document ?? ConfigDocument.Empty;
        return this with { Decoration = Decorated(document, accent, contrast), Groups = GroupOptions.From(document, contrast) };
    }

    private static DecorationOptions Decorated(ConfigDocument document, BorderColor? accent, HighContrast? contrast)
    {
        var decoration = DecorationOptions.From(document, contrast);
        return contrast is null ? decoration.WithAccent(accent) : decoration;
    }
}
