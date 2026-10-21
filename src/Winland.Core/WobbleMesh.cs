namespace Winland;

/// <summary>A rectangle in pixels with fractional edges, which the effects move by less than a pixel.</summary>
internal readonly record struct EffectBox(double X, double Y, double Width, double Height)
{
    internal double Right => X + Width;

    internal double Bottom => Y + Height;

    internal bool IsEmpty => Width <= 0 || Height <= 0;

    internal static EffectBox Of(Rectangle rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    internal EffectBox Union(EffectBox other)
    {
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        return new EffectBox(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }

    internal EffectBox Grow(double amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    /// <summary>The smallest whole-pixel rectangle that holds this one.</summary>
    internal Rectangle Enclosing()
    {
        var left = (int)Math.Floor(X);
        var top = (int)Math.Floor(Y);
        return new Rectangle(left, top, (int)Math.Ceiling(Right) - left, (int)Math.Ceiling(Bottom) - top);
    }
}

/// <summary>One corner of a triangle the wobble draws: a position in pixels and a place in the window's picture from 0 to 1.</summary>
internal readonly record struct MeshVertex(float X, float Y, float U, float V);

/// <summary>
/// The wobble of a window: a grid of points, each pushed by the window's motion against its direction and pulled back
/// to rest by a spring. It is Winland's deformable mesh.
/// </summary>
internal sealed class WobbleMesh
{
    private const double MaxJump = 1.5;
    private const double MinDisplacementLimit = 16;
    private const double MaxDisplacementLimit = 160;

    private int _edge;
    private double[] _dx = [];
    private double[] _dy = [];
    private double[] _vx = [];
    private double[] _vy = [];

    internal WobbleMesh(int verticesPerEdge = 8) => Resize(verticesPerEdge);

    /// <summary>The number of points along each edge, from 2 to 32.</summary>
    internal int Size => _edge;

    internal void Resize(int verticesPerEdge)
    {
        verticesPerEdge = Math.Clamp(verticesPerEdge, 2, 32);
        if (_edge == verticesPerEdge)
            return;

        _edge = verticesPerEdge;
        var count = _edge * _edge;
        _dx = new double[count];
        _dy = new double[count];
        _vx = new double[count];
        _vy = new double[count];
    }

    internal void Reset()
    {
        Array.Clear(_dx);
        Array.Clear(_dy);
        Array.Clear(_vx);
        Array.Clear(_vy);
    }

    /// <summary>The displacement of the point at <paramref name="x"/>, <paramref name="y"/>, for tests.</summary>
    internal (double X, double Y) Displacement(int x, int y) => (_dx[y * _edge + x], _dy[y * _edge + x]);

    /// <summary>
    /// Whether a move from <paramref name="previous"/> to <paramref name="current"/> is too far to wobble, which is a
    /// window jumping to another place rather than moving.
    /// </summary>
    internal static bool Jumps(EffectBox previous, EffectBox current)
    {
        var delta = Math.Max(
            Math.Max(Math.Abs(current.X - previous.X), Math.Abs(current.Y - previous.Y)),
            Math.Max(Math.Abs(current.Width - previous.Width), Math.Abs(current.Height - previous.Height)));
        var dimension = Math.Max(Math.Max(Math.Max(previous.Width, previous.Height), Math.Max(current.Width, current.Height)), 1);
        return delta > dimension * MaxJump;
    }

    /// <summary>
    /// Pushes every point against the window's move from <paramref name="previous"/> to <paramref name="current"/>.
    /// <paramref name="grab"/> is where the pointer holds the window, from 0 to 1 on each axis, or null: points
    /// near it follow the window and points far from it lag.
    /// </summary>
    internal void Push(EffectBox previous, EffectBox current, double intensity, (double X, double Y)? grab = null)
    {
        if (previous == current || previous.IsEmpty || current.IsEmpty)
            return;

        intensity = Math.Clamp(intensity, 0, 2);
        var limit = Math.Clamp(Math.Min(current.Width, current.Height) * 0.25, MinDisplacementLimit, MaxDisplacementLimit);
        var moveX = current.X - previous.X;
        var moveY = current.Y - previous.Y;
        var leftDelta = current.X - previous.X;
        var rightDelta = current.Right - previous.Right;
        var topDelta = current.Y - previous.Y;
        var bottomDelta = current.Bottom - previous.Bottom;

        var grabX = 0.0;
        var grabY = 0.0;
        var grabMax = 1.0;
        if (grab is { } held)
        {
            grabX = Math.Clamp(held.X, 0, 1);
            grabY = Math.Clamp(held.Y, 0, 1);
            grabMax = Math.Max(Math.Sqrt(Math.Pow(Math.Max(grabX, 1 - grabX), 2) + Math.Pow(Math.Max(grabY, 1 - grabY), 2)), 0.001);
        }

        for (var y = 0; y < _edge; y++)
        {
            var v = (double)y / (_edge - 1);
            for (var x = 0; x < _edge; x++)
            {
                var u = (double)x / (_edge - 1);
                var weightX = 0.45 + 0.55 * Math.Sin(v * Math.PI);
                var weightY = 0.45 + 0.55 * Math.Sin(u * Math.PI);
                var impulseX = -moveX * weightX - (leftDelta * (1 - u) + rightDelta * u);
                var impulseY = -moveY * weightY - (topDelta * (1 - v) + bottomDelta * v);
                if (grab is not null)
                {
                    var distance = Math.Sqrt((u - grabX) * (u - grabX) + (v - grabY) * (v - grabY));
                    var weight = Math.Clamp(distance / grabMax, 0, 1);
                    impulseX *= weight;
                    impulseY *= weight;
                }

                var index = y * _edge + x;
                _dx[index] += impulseX * intensity;
                _dy[index] += impulseY * intensity;
            }
        }

        for (var i = 0; i < _dx.Length; i++)
        {
            var length = Math.Sqrt(_dx[i] * _dx[i] + _dy[i] * _dy[i]);
            if (length <= limit || length <= 0)
                continue;

            var scale = limit / length;
            _dx[i] *= scale;
            _dy[i] *= scale;
        }
    }

    /// <summary>Lets every point spring back toward rest for <paramref name="seconds"/>.</summary>
    internal void Advance(WobbleSettings settings, double seconds)
    {
        for (var i = 0; i < _dx.Length; i++)
        {
            var x = 1 + _dx[i];
            var y = 1 + _dy[i];
            Spring.Advance(ref x, ref _vx[i], 1, settings.Stiffness, settings.Damping, settings.Mass, seconds);
            Spring.Advance(ref y, ref _vy[i], 1, settings.Stiffness, settings.Damping, settings.Mass, seconds);
            _dx[i] = x - 1;
            _dy[i] = y - 1;
        }
    }

    /// <summary>Whether every point is within the epsilons of rest and still.</summary>
    internal bool Stable(WobbleSettings settings)
    {
        for (var i = 0; i < _dx.Length; i++)
        {
            if (Math.Abs(_dx[i]) > settings.ValueEpsilon
                || Math.Abs(_dy[i]) > settings.ValueEpsilon
                || Math.Abs(_vx[i]) > settings.VelocityEpsilon
                || Math.Abs(_vy[i]) > settings.VelocityEpsilon)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The box that holds the window and every point of its mesh.</summary>
    internal EffectBox Extents(EffectBox box)
    {
        var left = box.X;
        var top = box.Y;
        var right = box.Right;
        var bottom = box.Bottom;
        for (var y = 0; y < _edge; y++)
        {
            for (var x = 0; x < _edge; x++)
            {
                var index = y * _edge + x;
                var pointX = box.X + box.Width * x / (_edge - 1) + _dx[index];
                var pointY = box.Y + box.Height * y / (_edge - 1) + _dy[index];
                left = Math.Min(left, pointX);
                top = Math.Min(top, pointY);
                right = Math.Max(right, pointX);
                bottom = Math.Max(bottom, pointY);
            }
        }

        return new EffectBox(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Fills <paramref name="triangles"/> with the window's picture laid over the deformed mesh, six vertices to each cell,
    /// as pixel positions of the screen and places in the picture from 0 to 1.
    /// </summary>
    internal void Triangles(EffectBox box, List<MeshVertex> triangles)
    {
        triangles.Clear();
        for (var y = 0; y < _edge - 1; y++)
        {
            for (var x = 0; x < _edge - 1; x++)
            {
                var a = Vertex(box, x, y);
                var b = Vertex(box, x, y + 1);
                var c = Vertex(box, x + 1, y);
                var d = Vertex(box, x + 1, y + 1);
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(c);
                triangles.Add(b);
                triangles.Add(d);
            }
        }
    }

    private MeshVertex Vertex(EffectBox box, int x, int y)
    {
        var u = (double)x / (_edge - 1);
        var v = (double)y / (_edge - 1);
        var index = y * _edge + x;
        return new MeshVertex(
            (float)(box.X + box.Width * u + _dx[index]),
            (float)(box.Y + box.Height * v + _dy[index]),
            (float)u,
            (float)v);
    }
}
