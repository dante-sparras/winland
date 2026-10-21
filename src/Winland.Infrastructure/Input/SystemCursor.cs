using Windows.Win32;

namespace Winland.Infrastructure;

public sealed class SystemCursor : ICursorPosition
{
    public Point Read() =>
        PInvoke.GetCursorPos(out var cursor) ? new Point(cursor.X, cursor.Y) : new Point(0, 0);
}
