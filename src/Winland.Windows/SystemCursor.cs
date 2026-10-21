using Windows.Win32;
using Winland.Core;

namespace Winland.Windows;

public sealed class SystemCursor : ICursorPosition
{
    public Point Read() =>
        PInvoke.GetCursorPos(out var cursor) ? new Point(cursor.X, cursor.Y) : new Point(0, 0);
}
