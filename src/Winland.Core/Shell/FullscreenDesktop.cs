namespace Winland.Core.Shell;

/// <summary>Whether the Bar is drawn and whether Spacing is on.</summary>
/// <param name="BarVisible">True while the Bar is shown.</param>
/// <param name="Spacing">True while there is space between the tiles.</param>
public readonly record struct DesktopLook(bool BarVisible, bool Spacing);

/// <summary>
/// Omarchy's full screen desktop: one Chord takes the Bar and the Spacing away together, and gives both back.
/// What is given back is what was there before, so a Host that keeps Spacing off gets only its Bar again.
/// </summary>
public sealed class FullscreenDesktop
{
    private DesktopLook? _before;

    /// <summary>The look the Chord asks for, given the look the desktop has now.</summary>
    /// <param name="now">The Bar and the Spacing as they are.</param>
    /// <returns>
    /// Neither, while either is there. Once both are gone, what the last press took away. When this desktop took nothing away,
    /// such as after a restart, the Bar comes back and Spacing stays off.
    /// </returns>
    public DesktopLook Toggle(DesktopLook now)
    {
        if (now.BarVisible || now.Spacing)
        {
            _before = now;
            return new DesktopLook(false, false);
        }

        var back = _before ?? new DesktopLook(true, false);
        _before = null;
        return back;
    }
}
