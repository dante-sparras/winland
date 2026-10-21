namespace Winland.Domain;

/// <summary>What happens to a window when its motion lands.</summary>
public enum MotionEnd
{
    /// <summary>The window stays where the motion left it, which is its Layout rectangle.</summary>
    None,

    /// <summary>The window is hidden and then put back on <see cref="Motion.Rest"/>.</summary>
    Hide,

    /// <summary>The window is asked to close. If it stays open it is put back on <see cref="Motion.Rest"/>.</summary>
    Close,
}
