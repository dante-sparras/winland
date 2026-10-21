namespace Winland.Domain;

/// <summary>
/// What one window shows at one moment of its motion. A null part is not animating and is left alone.
/// </summary>
public sealed record Frame(WindowId Window, long Generation)
{
    public Rectangle? Bounds { get; init; }

    /// <summary>Where <see cref="Bounds"/> is heading. Null when the geometry is not animating.</summary>
    public Rectangle? Goal { get; init; }

    /// <summary>
    /// The Layout rectangle the window rests on. Its size is the size the window keeps for the whole motion, which
    /// is not the size of <see cref="Bounds"/> while the motion grows, shrinks or squeezes the window. Null when the
    /// geometry is not animating.
    /// </summary>
    public Rectangle? Content { get; init; }

    public byte? Alpha { get; init; }

    public BorderColor? Border { get; init; }

    /// <summary>The motion has landed. <see cref="End"/> is performed and the window rests on <see cref="Rest"/>.</summary>
    public bool Done { get; init; }

    public MotionEnd End { get; init; }

    public Rectangle Rest { get; init; }

    /// <summary>The window's alpha once it rests, without any fade.</summary>
    public byte RestAlpha { get; init; } = 255;
}
