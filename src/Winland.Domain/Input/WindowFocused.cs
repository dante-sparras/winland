using System.Globalization;

namespace Winland.Domain;

public sealed record WindowFocused(WindowId Window, FocusCause Cause = FocusCause.User) : Event
{
    /// <summary>The top-level window the click before a <see cref="FocusCause.User"/> change landed on, if it was a click.</summary>
    public WindowId? Clicked { get; init; }
}
