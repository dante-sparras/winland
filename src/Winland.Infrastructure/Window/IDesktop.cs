namespace Winland.Infrastructure;

/// <summary>How a placement reaches the window.</summary>
internal enum Delivery
{
    /// <summary>The call returns when the window has taken it.</summary>
    Wait,

    /// <summary>The window takes it when its thread gets to it.</summary>
    Post,

    /// <summary>Posted, and the window keeps the size it has.</summary>
    PostKeepingSize,
}

internal enum ShowAs
{
    Hide,
    Show,
    ShowMinimized,
}

/// <summary>
/// The windows of the desktop as the Windows adapter carries out an Outcome on them, one method for each thing it
/// asks Windows to do or reads from it. It makes no choices: which window to leave alone, when to wait for one and
/// what order to do things in are the adapter's, so a test that replaces this sees all of them. A style, a frame and
/// DWM's backdrop and region are the three smaller ports.
/// </summary>
internal interface IDesktop : IWindowStyles, IWindowFrame, IDwmSurface
{
    bool Exists(WindowId window);

    bool Shown(WindowId window);

    bool Minimized(WindowId window);

    /// <summary>Whether the window's program has stopped answering, so a call that waits for it would block.</summary>
    bool Hung(WindowId window);

    int Dpi(WindowId window);

    /// <summary>Whether the window has an area it has not painted yet.</summary>
    bool Repainting(WindowId window);

    /// <summary>The window rectangle, which includes the invisible frame DWM draws around it.</summary>
    bool TryOuter(WindowId window, out Rectangle outer);

    /// <summary>The frame the window shows.</summary>
    bool TryVisible(WindowId window, out Rectangle visible);

    void SetAttribute(WindowId window, int attribute, uint value);

    /// <summary>Moves and sizes the window by its outer rectangle, without changing its place in the z-order or activating it.</summary>
    void SetBounds(WindowId window, Rectangle outer, Delivery delivery);

    /// <summary>Hides or shows the window and waits for it.</summary>
    void Show(WindowId window, ShowAs show);

    /// <summary>Hides or shows the window without waiting for it.</summary>
    void ShowAsync(WindowId window, ShowAs show);

    /// <summary>Puts the window above every window that is not topmost, or below all of them.</summary>
    void Raise(WindowId window, bool top);

    /// <summary>Raises the window without waiting for it.</summary>
    void RaiseAsync(WindowId window, bool top);

    /// <summary>Puts the window among the topmost ones. False, and nothing done, when it is topmost already.</summary>
    bool HoldOnTop(WindowId window);

    /// <summary>Takes the window out of the topmost ones, above every window that is not topmost.</summary>
    void LetGo(WindowId window);

    /// <summary>Gives the window the foreground, by whatever Windows takes. Returns whether it has it.</summary>
    bool Foreground(WindowId window);

    /// <summary>Posts the window the message that asks it to close.</summary>
    void Close(WindowId window);

    /// <summary>Waits for the next composed frame.</summary>
    void Pace();

    /// <summary>A window of Winland's own that shows pictures of <paramref name="window"/>, or null when none could be made.</summary>
    IStandInSurface? OpenStandIn(WindowId window);

    /// <summary>Ends the thread that owns the stand-in windows, and with it every one.</summary>
    void StopStandIns();
}

internal enum StandInSlot
{
    Picture,
    Right,
    Bottom,
}

/// <summary>Winland's own window that stands in for a window of another program, and the DWM thumbnails of it that it shows.</summary>
internal interface IStandInSurface
{
    /// <summary>The stand-in window's handle, which a border stacks on.</summary>
    nint Handle { get; }

    /// <summary>The size of the window's outer rectangle as it is painted now.</summary>
    Size? PictureSize();

    /// <summary>Puts the stand-in on <paramref name="bounds"/>, just above the window it stands in for, shown, and waits for the move.</summary>
    void Place(Rectangle bounds);

    /// <summary>Draws <paramref name="piece"/> in the slot, or takes the slot's picture away when it is null.</summary>
    void Show(StandInSlot slot, StandInPiece? piece, byte alpha);

    void SetAlpha(byte alpha);

    void Hide();

    /// <summary>Takes the stand-in away. Closing it again does nothing.</summary>
    void Close();
}
