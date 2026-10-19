namespace Winland;

public enum GestureDirection
{
    Swipe,
    Left,
    Right,
    Up,
    Down,
    Vertical,
    Horizontal,
    Pinch,
    PinchIn,
    PinchOut,
}

/// <summary>
/// One <c>gesture</c> line. Every action is kept so a later line is shadowed and <c>unset</c> matches as configured,
/// but only the honored ones run. <paramref name="Argument"/> is the special workspace's name, the dispatcher, the
/// float or fullscreen mode, or the number of the Lua function a <c>luafunction</c> gesture calls.
/// </summary>
public sealed record TrackpadGesture(
    int Fingers,
    GestureDirection Direction,
    Modifier Modifiers,
    double Scale,
    string Action,
    bool DisableInhibit = false,
    string Argument = "",
    string DispatchArguments = "")
{
    public const string WorkspaceAction = "workspace";

    public const string SpecialAction = "special";

    public const string DispatcherAction = "dispatcher";

    public const string CloseAction = "close";

    public const string FloatAction = "float";

    public const string FullscreenAction = "fullscreen";

    public const string ScrollMoveAction = "scrollMove";

    /// <summary>Winland's own action: a function of the Lua config, kept under the number the line names, follows the swipe.</summary>
    public const string FunctionAction = "luafunction";

    public bool IsFunction => Action == FunctionAction;

    public bool IsScrollMove => Action == ScrollMoveAction;

    public bool IsWorkspace => Action == WorkspaceAction;

    public bool IsSpecial => Action == SpecialAction;

    /// <summary>An action that runs once the fingers lift: a dispatcher, or close, float or fullscreen far enough.</summary>
    public bool IsOnRelease => Action is DispatcherAction or CloseAction or FloatAction or FullscreenAction;

    public bool IsHonored => IsWorkspace || IsSpecial || IsOnRelease || IsScrollMove || IsFunction;

    /// <summary>
    /// How far close must travel, as the pinned release measures it: a fifth of 200 pixels for close and of 250 for
    /// float and fullscreen. A dispatcher runs at any distance.
    /// </summary>
    internal double ReleaseDistance => Action switch
    {
        CloseAction => 0.2 * 200,
        FloatAction or FullscreenAction => 0.2 * 250,
        _ => double.NegativeInfinity,
    };

    internal static bool TryDirection(string text, out GestureDirection direction)
    {
        switch (text.ToLowerInvariant())
        {
            case "swipe":
                direction = GestureDirection.Swipe;
                return true;
            case "left" or "l":
                direction = GestureDirection.Left;
                return true;
            case "right" or "r":
                direction = GestureDirection.Right;
                return true;
            case "up" or "u" or "top" or "t":
                direction = GestureDirection.Up;
                return true;
            case "down" or "d" or "bottom" or "b":
                direction = GestureDirection.Down;
                return true;
            case "horizontal" or "horiz":
                direction = GestureDirection.Horizontal;
                return true;
            case "vertical" or "vert":
                direction = GestureDirection.Vertical;
                return true;
            case "pinch":
                direction = GestureDirection.Pinch;
                return true;
            case "pinchin" or "zoomin":
                direction = GestureDirection.PinchIn;
                return true;
            case "pinchout" or "zoomout":
                direction = GestureDirection.PinchOut;
                return true;
            default:
                direction = default;
                return false;
        }
    }

    internal static bool IsPinch(GestureDirection direction) =>
        direction is GestureDirection.Pinch or GestureDirection.PinchIn or GestureDirection.PinchOut;

    internal static GestureDirection Axis(GestureDirection direction) => direction switch
    {
        GestureDirection.Up or GestureDirection.Down or GestureDirection.Vertical => GestureDirection.Vertical,
        GestureDirection.Left or GestureDirection.Right or GestureDirection.Horizontal => GestureDirection.Horizontal,
        GestureDirection.Pinch or GestureDirection.PinchIn or GestureDirection.PinchOut => GestureDirection.Pinch,
        _ => GestureDirection.Swipe,
    };

    /// <summary>A gesture already configured hides a new one with the same fingers and modifiers on an overlapping direction.</summary>
    internal bool Shadows(TrackpadGesture next)
    {
        if (Fingers != next.Fingers || Modifiers != next.Modifiers)
            return false;

        var axis = Axis(next.Direction);
        return Direction == axis
            || Direction == next.Direction
            || ((axis is GestureDirection.Vertical or GestureDirection.Horizontal) && Direction == GestureDirection.Swipe);
    }

    /// <summary>The distance one update moves along this direction, before the delta scale.</summary>
    internal static double Along(GestureDirection direction, double x, double y) => direction switch
    {
        GestureDirection.Left => -x,
        GestureDirection.Right or GestureDirection.Horizontal => x,
        GestureDirection.Up => -y,
        GestureDirection.Down or GestureDirection.Vertical => y,
        _ => Math.Sqrt((x * x) + (y * y)),
    };
}

/// <summary>The phase of a swipe a <see cref="GestureCall"/> reports.</summary>
public enum GesturePhase
{
    Start,
    Update,
    End,
}

/// <summary>
/// One phase of a swipe that a <c>luafunction</c> gesture took, for the host to hand the Lua config's function
/// numbered <paramref name="Function"/>. A start and an update carry the fingers and the delta of the update that
/// made them, and an end whether it was cancelled. A start and an update have the direction the fingers took, and an
/// end the configured one, as in the pinned release.
/// </summary>
public sealed record GestureCall(
    string Function,
    GesturePhase Phase,
    GestureDirection Direction,
    long TimeMs,
    int Fingers = 0,
    double DeltaX = 0,
    double DeltaY = 0,
    bool Cancelled = false);
