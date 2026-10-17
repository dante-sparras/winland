namespace Winland;

public sealed partial class Core
{
    // A special swipe is whole at this distance, and opens or closes once it has covered this share of it.
    private const double SpecialSwipeDistance = 150;
    private const double SpecialSwipeRatio = 0.3;

    private SpecialSwipe? _specialSwipe;

    /// <summary>
    /// Shows the gesture's special workspace on the focused Monitor, or holds the one already shown so the swipe can
    /// close it. The swipe has a session only while that Monitor shows that special workspace.
    /// </summary>
    private Outcome? BeginSpecialSwipe(TrackpadGesture gesture)
    {
        _specialSwipe = null;
        if (SessionLock?.Locked == true || _keyState.Inhibited)
            return null;

        var target = SpecialName(gesture.Argument);
        var shownOn = FindWorkspace(target) is null ? null : MonitorShowing(target);
        var closing = shownOn is not null;
        var monitor = shownOn ?? FocusedId();
        if (_monitors.Count > 0 && MonitorById(monitor) is not { Effective.Enabled: true })
            return null;

        var opened = closing ? null : OpenSpecial(target, null, dispatcher: null, allowsElevation: false);
        if (MonitorShowing(target) != monitor)
            return opened;

        _specialSwipe = new SpecialSwipe(target, monitor, closing);
        return opened;
    }

    private void StepSpecialSwipe(double distance)
    {
        if (_specialSwipe is not { } swipe)
            return;

        // The special workspace was hidden, moved or replaced by something else, so the swipe lets go of it.
        if (!OwnsSpecial(swipe))
        {
            _specialSwipe = null;
            return;
        }

        swipe.Delta += distance;
    }

    private Outcome EndSpecialSwipe()
    {
        if (_specialSwipe is not { } swipe)
            return View([]);

        _specialSwipe = null;
        var completion = Math.Clamp(swipe.Delta / SpecialSwipeDistance, 0, 1);
        var close = completion < SpecialSwipeRatio ? !swipe.Closing : swipe.Closing;
        if (!close || !OwnsSpecial(swipe))
            return View([]);

        return CloseSpecial(swipe.Monitor, dispatcher: null, allowsElevation: false);
    }

    private bool OwnsSpecial(SpecialSwipe swipe) =>
        FindWorkspace(swipe.Workspace) is not null && MonitorShowing(swipe.Workspace) == swipe.Monitor;

    private sealed class SpecialSwipe(WorkspaceId workspace, MonitorId monitor, bool closing)
    {
        public WorkspaceId Workspace { get; } = workspace;

        public MonitorId Monitor { get; } = monitor;

        /// <summary>The special workspace was shown when the swipe began, so a whole swipe hides it.</summary>
        public bool Closing { get; } = closing;

        public double Delta { get; set; }
    }
}
