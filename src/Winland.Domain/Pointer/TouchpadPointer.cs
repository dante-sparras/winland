namespace Winland.Domain;

/// <summary>
/// Tells which wheel turns and cursor moves came from the precision touchpad, from the fingers its raw reports show,
/// because Windows hands them to the mouse hook with no device. One finger or more on the pad moves the cursor, and two
/// or more scroll. Windows keeps scrolling a moment after the fingers lift, so a wheel turn that is not a whole notch
/// within <see cref="CoastMs"/> of the last two-finger report is still the touchpad's. Used on the hook thread.
/// </summary>
public static class TouchpadPointer
{
    /// <summary>How long a report of fingers on the pad stands. The pad reports many times a second while touched.</summary>
    public const long StaleMs = 250;

    /// <summary>How long after two fingers lift a part-notch wheel turn is still the touchpad's coasting scroll.</summary>
    public const long CoastMs = 2000;

    /// <summary>One notch of a mouse wheel.</summary>
    public const int Notch = 120;

    /// <summary>Whether a cursor move is the touchpad's, given the fingers last reported and how long ago.</summary>
    public static bool Moves(int fingers, long sinceReportMs) => fingers >= 1 && sinceReportMs <= StaleMs;

    /// <summary>Whether a wheel turn of <paramref name="delta"/> is the touchpad's.</summary>
    public static bool Scrolls(int fingers, long sinceReportMs, long sinceTwoFingersMs, int delta)
    {
        if (fingers >= 2 && sinceReportMs <= StaleMs)
            return true;

        return sinceTwoFingersMs <= CoastMs && delta % Notch != 0;
    }
}
