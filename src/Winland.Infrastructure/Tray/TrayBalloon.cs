namespace Winland.Infrastructure;

/// <summary>
/// Whether the notification-area icon and its config balloon should be up. The host applies one of these.
/// A shown icon keeps the balloon on itself. A hidden icon exists only while the balloon does, and a
/// dismissed balloon takes that icon away. The same <see cref="ConfigNotice.Generation"/> is not shown again.
/// </summary>
internal static class TrayBalloon
{
    internal readonly record struct Plan(bool ShowIcon, bool Temporary, bool ShowBalloon, bool ClearBalloon);

    internal static Plan For(bool trayShown, ConfigNotice? notice, int shownGeneration, int closedGeneration)
    {
        if (trayShown)
        {
            if (notice is null)
                return new(true, false, false, shownGeneration != 0);

            if (notice.Generation == shownGeneration || notice.Generation == closedGeneration)
                return new(true, false, false, false);

            return new(true, false, true, false);
        }

        if (notice is null || notice.Generation == closedGeneration)
            return new(false, false, false, false);

        if (notice.Generation == shownGeneration)
            return new(true, true, false, false);

        return new(true, true, true, false);
    }
}
