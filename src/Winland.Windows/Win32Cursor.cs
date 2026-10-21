using Windows.Win32;

namespace Winland.Windows;

/// <summary>Moves and hides the cursor. Tests do not construct this.</summary>
// Stryker disable once all : moves and hides the real cursor.
internal sealed class Win32Cursor : ICursorSurface
{
    private bool _hidden;

    public void Warp(Point position) => _ = PInvoke.SetCursorPos(position.X, position.Y);

    public void Hide()
    {
        if (_hidden)
            return;

        _hidden = true;
        Magnifier.HideCursor(true);
    }

    public void Show()
    {
        if (!_hidden)
            return;

        _hidden = false;
        Magnifier.HideCursor(false);
    }
}
