namespace Winland.Domain;

public sealed record SessionOptions
{
    public bool FocusOnActivate { get; init; }

    /// <summary><c>general:tray_icon</c>: the notification area shows the focused Workspace's number.</summary>
    public bool ShowTray { get; init; } = true;

    public bool HideSpecialOnWorkspaceChange { get; init; }

    /// <summary><c>misc:disable_autoreload</c>: saving the config does not reload it.</summary>
    public bool DisableAutoreload { get; init; }

    /// <summary><c>binds:allow_pin_fullscreen</c>: a pinned Client can go fullscreen, and is pinned again when it leaves that mode.</summary>
    public bool AllowPinFullscreen { get; init; }

    /// <summary>
    /// <c>general:hide_taskbar</c>: hide the taskbars and set them to auto-hide, so Clients tile into the strip they reserved.
    /// </summary>
    public bool HideTaskbar { get; init; }

    /// <summary><c>binds:movefocus_cycles_fullscreen</c>: movefocus from a fullscreen Client cycles its Workspace's windows instead of looking in the direction.</summary>
    public bool MoveFocusCyclesFullscreen { get; init; }

    /// <summary><c>misc:font_family</c>: the font of the text Winland draws, where nothing names another.</summary>
    public string FontFamily { get; init; } = "Sans";

    public static SessionOptions Documented { get; } = new();

    internal static OptionSet<SessionOptions> Options { get; } = new(
    [
        Of(TrayIcon.Keyword, OptionValue.Bool, o => o.ShowTray, (o, v) => o with { ShowTray = v }),
        Of("misc:focus_on_activate", OptionValue.Bool, o => o.FocusOnActivate, (o, v) => o with { FocusOnActivate = v }),
        Of("binds:hide_special_on_workspace_change", OptionValue.Bool, o => o.HideSpecialOnWorkspaceChange, (o, v) => o with { HideSpecialOnWorkspaceChange = v }),
        Of("misc:disable_autoreload", OptionValue.Bool, o => o.DisableAutoreload, (o, v) => o with { DisableAutoreload = v }),
        Of("binds:allow_pin_fullscreen", OptionValue.Bool, o => o.AllowPinFullscreen, (o, v) => o with { AllowPinFullscreen = v }),
        Of("general:hide_taskbar", OptionValue.Bool, o => o.HideTaskbar, (o, v) => o with { HideTaskbar = v }),
        Of("binds:movefocus_cycles_fullscreen", OptionValue.Bool, o => o.MoveFocusCyclesFullscreen, (o, v) => o with { MoveFocusCyclesFullscreen = v }),
        Of("misc:font_family", OptionValue.Text, o => o.FontFamily, (o, v) => o with { FontFamily = v }),
    ]);

    public static SessionOptions From(ConfigDocument document) => Options.From(Documented, document);

    private static Option<SessionOptions> Of<T>(string name, OptionValue<T> value, Func<SessionOptions, T> get, Func<SessionOptions, T, SessionOptions> set) =>
        Option<SessionOptions>.Of(name, value, get, set);
}
