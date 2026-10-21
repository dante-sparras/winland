namespace Winland.Domain;

public readonly record struct WindowTraits(
    bool Visible,
    bool Titled,
    bool TopLevel,
    bool Tool,
    bool Tooltip,
    bool Taskbar,
    bool Cloaked,
    bool Dialog,
    bool Elevated,
    string Class = "",
    string Title = "",
    string Exe = "",
    string Aumid = "",
    Size? MinSize = null,
    Size? MaxSize = null,
    bool ExclusiveFullscreen = false,
    bool Overlay = false,
    string? InitialTitle = null,
    string? InitialClass = null)
{
    public static WindowTraits Normal { get; } = new(
        Visible: true,
        Titled: true,
        TopLevel: true,
        Tool: false,
        Tooltip: false,
        Taskbar: false,
        Cloaked: false,
        Dialog: false,
        Elevated: false);
}
