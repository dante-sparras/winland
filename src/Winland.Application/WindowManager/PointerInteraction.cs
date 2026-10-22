namespace Winland.Application;

/// <summary>
/// What the pointer is doing to a window: a drag (<see cref="Gesture"/>), a resize by the border
/// (<see cref="BorderDrag"/>), or a move or resize the system runs (<see cref="NativeMove"/>, <see cref="NativeSize"/>).
/// One runs at a time. A new one starts only when none is running, and the one running ends when its window closes.
/// </summary>
public sealed partial class WindowManager
{
    private PointerInteraction? _interaction;

    private abstract class PointerInteraction(WindowId window)
    {
        public WindowId Window { get; } = window;
    }
}
