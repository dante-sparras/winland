namespace Winland.Domain;

/// <summary>
/// <c>input:scroll_method on_button_down</c>: while <see cref="Button"/> is held, moving the pointer scrolls instead.
/// With <see cref="Lock"/> a click of the button turns the scrolling on until the next click.
/// </summary>
public sealed record ButtonScrollOptions(bool Enabled, string Button, bool Lock, bool Natural)
{
    public static ButtonScrollOptions Off { get; } = new(false, "mouse:274", false, false);
}
