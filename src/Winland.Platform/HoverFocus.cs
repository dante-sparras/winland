namespace Winland.Platform;

/// <summary>
/// Turns off "activate the window under the pointer" while the keyboard owns input, then puts that setting back.
/// </summary>
internal sealed class HoverFocus
{
    private const int GetTracking = 0x1000;
    private const int SetTracking = 0x1001;

    private readonly object _gate = new();
    private bool _known;
    private bool _userWants;
    private bool _suspended;

    /// <summary>Stops the pointer from changing the active window, when the user has that setting on.</summary>
    public void Suspend()
    {
        lock (_gate)
        {
            if (!Known() || !_userWants || _suspended)
            {
                return;
            }

            if (Apply(false))
            {
                _suspended = true;
            }
        }
    }

    /// <summary>Restores the setting captured before the first suspend.</summary>
    public void Resume()
    {
        lock (_gate)
        {
            if (!_suspended)
            {
                return;
            }

            if (Apply(_userWants))
            {
                _suspended = false;
            }
        }
    }

    private bool Known()
    {
        if (_known)
        {
            return true;
        }

        var value = 0;
        if (!Native.SystemParametersInfo(GetTracking, 0, ref value, 0))
        {
            return false;
        }

        _userWants = value != 0;
        _known = true;
        return true;
    }

    // This setting travels in pvParam. Passed in uiParam it reads as off, and a resume could never turn it back on.
    private static bool Apply(bool on) =>
        Native.SystemParametersInfoFlag(SetTracking, 0, on ? 1 : 0, 0);
}
