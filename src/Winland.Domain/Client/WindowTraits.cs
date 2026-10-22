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

    /// <summary>
    /// These traits with the initial class and title set to the current ones when they are not set yet. initialClass and
    /// initialTitle match what a window had when it appeared, including an empty one, so a later one does not stand in.
    /// </summary>
    public WindowTraits WithInitialNames() => this with
    {
        InitialClass = InitialClass ?? Class,
        InitialTitle = InitialTitle ?? Title,
    };
}
