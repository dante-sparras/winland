namespace Winland.Windows.Tests;

public class MouseHookTests
{
    private const uint XDown = 0x020B;
    private const uint XUp = 0x020C;

    [Theory]
    [InlineData(0x0201u, "mouse:272", false)]
    [InlineData(0x0202u, "mouse:272", true)]
    [InlineData(0x0204u, "mouse:273", false)]
    [InlineData(0x0205u, "mouse:273", true)]
    [InlineData(0x0207u, "mouse:274", false)]
    [InlineData(0x0208u, "mouse:274", true)]
    public void MainButtonsAreNamedByMessage(uint message, string expected, bool expectedUp)
    {
        Assert.True(MouseHook.TryButton(message, 0, out var button, out var up));
        Assert.Equal(expected, button);
        Assert.Equal(expectedUp, up);
    }

    // The side button is in the high word of mouseData. The low word is reserved and may hold anything.
    [Theory]
    [InlineData(XDown, 0x0001_0000u, "mouse:275", false)]
    [InlineData(XUp, 0x0001_0000u, "mouse:275", true)]
    [InlineData(XDown, 0x0002_0000u, "mouse:276", false)]
    [InlineData(XUp, 0x0002_0000u, "mouse:276", true)]
    [InlineData(XDown, 0x0001_0002u, "mouse:275", false)]
    [InlineData(XDown, 0x0002_0001u, "mouse:276", false)]
    public void SideButtonsAreReadFromTheHighWord(uint message, uint mouseData, string expected, bool expectedUp)
    {
        Assert.True(MouseHook.TryButton(message, mouseData, out var button, out var up));
        Assert.Equal(expected, button);
        Assert.Equal(expectedUp, up);
    }

    [Theory]
    [InlineData(0x0200u)]
    [InlineData(0x020Au)]
    [InlineData(0x020Eu)]
    public void OtherMessagesAreNotButtons(uint message) =>
        Assert.False(MouseHook.TryButton(message, 0x0001_0000u, out _, out _));

    [Theory]
    [InlineData(0x020Au, 120, "mouse_up")]
    [InlineData(0x020Au, -120, "mouse_down")]
    [InlineData(0x020Eu, 120, "mouse_right")]
    [InlineData(0x020Eu, -120, "mouse_left")]
    [InlineData(0x020Au, 1, "mouse_up")]
    [InlineData(0x020Eu, -1, "mouse_left")]
    public void WheelDirectionIsTheSignOfTheHighWord(uint message, int delta, string expected)
    {
        var mouseData = (uint)(ushort)(short)delta << 16;

        Assert.True(MouseHook.TryWheel(message, mouseData, out var key));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void AStillWheelOrAnotherMessageIsNotATurn()
    {
        Assert.False(MouseHook.TryWheel(0x020A, 0x0000_FFFF, out var still));
        Assert.False(MouseHook.TryWheel(0x0201, 120u << 16, out var other));
        Assert.False(MouseHook.TryButton(0x0200, 0, out var moved, out _));

        Assert.Equal("", still);
        Assert.Equal("", other);
        Assert.Equal("", moved);
    }

    // A button bound to movewindow with no argument or to resizewindow drags. movewindow with a
    // direction moves the window once.
    [Theory]
    [InlineData("movewindow", "", true)]
    [InlineData("MoveWindow", "", true)]
    [InlineData("movewindow", "l", false)]
    [InlineData("resizewindow", "", true)]
    [InlineData("ResizeWindow", "1", true)]
    [InlineData("killactive", "", false)]
    public void OnlyAWindowDragIsADrag(string dispatcher, string arguments, bool drag)
    {
        Assert.Equal(drag, MouseHook.IsDrag(dispatcher, arguments));
    }

    [Fact]
    public void ASingleDragDispatcherIsTheDrag()
    {
        Assert.True(MouseHook.TryDrag(new BindDecision(true, "resizewindow", "1"), out var dispatcher, out var arguments));
        Assert.Equal(("resizewindow", "1"), (dispatcher, arguments));

        Assert.False(MouseHook.TryDrag(new BindDecision(true, "killactive"), out dispatcher, out arguments));
        Assert.Equal(("", ""), (dispatcher, arguments));
        Assert.False(MouseHook.TryDrag(new BindDecision(false, null), out dispatcher, out arguments));
        Assert.Equal(("", ""), (dispatcher, arguments));
        Assert.False(MouseHook.TryDrag(new BindDecision(false, ""), out _, out _));
    }

    // A button bound to several dispatchers drags with the first that drags.
    [Fact]
    public void TheFirstDragAmongSeveralDispatchersIsTheDrag()
    {
        var several = new BindDecision(true, "killactive")
        {
            Dispatches = [new("killactive", ""), new("movewindow", "r"), new("movewindow", ""), new("resizewindow", "")],
        };
        var none = new BindDecision(true, "killactive")
        {
            Dispatches = [new("killactive", ""), new("movewindow", "r")],
        };

        Assert.True(MouseHook.TryDrag(several, out var dispatcher, out var arguments));
        Assert.Equal(("movewindow", ""), (dispatcher, arguments));
        Assert.False(MouseHook.TryDrag(none, out dispatcher, out arguments));
        Assert.Equal(("", ""), (dispatcher, arguments));
    }
}
