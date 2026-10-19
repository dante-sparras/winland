namespace Winland;

/// <summary>
/// <c>input:rotation</c>: the mouse's motion turns clockwise by that many degrees, as the pinned release rotates a
/// pointer device. What rounding leaves of each move is kept for the next, so slow moves are not lost. Used on the hook
/// thread.
/// </summary>
public sealed class PointerRotation
{
    private double _restX;
    private double _restY;
    private int _degrees;

    /// <summary>The move the cursor makes instead of <paramref name="dx"/>, <paramref name="dy"/>.</summary>
    public (int Dx, int Dy) Turn(int dx, int dy, int degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        if (degrees != _degrees)
        {
            _degrees = degrees;
            _restX = 0;
            _restY = 0;
        }

        if (degrees == 0)
            return (dx, dy);

        // Screen y grows downwards, so a clockwise turn on screen is this rotation.
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = (dx * cos) - (dy * sin) + _restX;
        var y = (dx * sin) + (dy * cos) + _restY;
        var turnedX = (int)Math.Round(x, MidpointRounding.AwayFromZero);
        var turnedY = (int)Math.Round(y, MidpointRounding.AwayFromZero);
        _restX = x - turnedX;
        _restY = y - turnedY;
        return (turnedX, turnedY);
    }
}
