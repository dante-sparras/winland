namespace Winland.Application.Tests;

/// <summary>
/// A window that asks to activate another. On Windows the source holds the foreground, so the system lets it hand the
/// foreground to the target with or without input: Winland hears the target take it, after a click as a user's change
/// and otherwise as an activation, and honors only the user's change.
/// </summary>
public class FocusOnActivateTests
{
    private const string Source = "xdg-activation-source";
    private const string Target = "xdg-activation-target";

    [Fact]
    public void ActivationIsHonoredOnlyAfterUserInput()
    {
        var desk = new TestDesktop("misc:focus_on_activate = true\ninput:follow_mouse = 0\n");
        desk.Spawn(Source);
        var target = desk.Spawn(Target);
        Assert.Equal(2, desk.Clients().Length);
        FocusSource(desk);

        // Activation without set_serial.
        desk.Handle(new WindowFocused(target, FocusCause.Activated));
        Assert.Equal(Source, desk.ActiveClass());
        Assert.Equal(2, desk.Clients().Length);

        // Activation with a real pointer-button serial.
        FocusSource(desk);
        MoveCursorToSource(desk);
        desk.Click();
        Assert.Equal(Source, desk.ActiveClass());

        desk.Handle(new WindowFocused(target, FocusCause.User));
        Assert.Equal(Target, desk.ActiveClass());

        // Activation with an already-consumed serial.
        FocusSource(desk);
        desk.Handle(new WindowFocused(target, FocusCause.Activated));
        Assert.Equal(Source, desk.ActiveClass());
        Assert.Equal(2, desk.Clients().Length);
    }

    private static void FocusSource(TestDesktop desk)
    {
        desk.Ok("focuswindow", $"class:^{Source}$");
        Assert.Equal(Source, desk.ActiveClass());
    }

    private static void MoveCursorToSource(TestDesktop desk)
    {
        var bounds = desk.Placed(desk.ActiveId());
        desk.MovePointer(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }
}
