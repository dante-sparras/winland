namespace Winland.Windows;

/// <summary>
/// Draws a captured window for one frame of its effects: the picture laid over the wobbling mesh, then averaged over the boxes of
/// the motion blur. The result is premultiplied pixels of the frame's canvas, which <see cref="GpuDevice.ReadRows"/> receives.
/// </summary>
internal sealed unsafe class EffectRenderer : IDisposable
{
    // The largest canvas drawn, by a side and by its pixels. A window past this is shown without the effects.
    private const int MaxSide = 16384;
    private const long MaxPixels = 48_000_000;

    private readonly GpuDevice _gpu;
    private readonly List<MeshVertex> _triangles = [];
    private readonly float[] _constants = new float[12];
    private GpuTexture? _scene;
    private GpuTexture? _output;
    private GpuTexture? _staging;
    private MeshVertex[] _vertices = new MeshVertex[6];

    internal EffectRenderer(GpuDevice gpu) => _gpu = gpu;

    /// <summary>
    /// Takes a frame: its premultiplied pixels, which are rows of four bytes each <paramref name="pitch"/> bytes apart, and the
    /// rectangle of the screen they belong in. Returns whether it showed them.
    /// </summary>
    internal delegate bool FrameSink(byte* rows, int pitch, Rectangle canvas);

    /// <summary>
    /// Draws <paramref name="source"/>, a part of <paramref name="picture"/> in its pixels, in <paramref name="rest"/> on the screen,
    /// and gives the pixels of the frame to <paramref name="sink"/>. The caller holds the device's gate.
    /// </summary>
    internal bool Render(GpuTexture picture, Rectangle source, EffectTrack track, EffectBox rest, double now, FrameSink sink)
    {
        var frame = track.Frame(rest, now);
        var canvas = frame.Canvas.Enclosing();
        if (canvas.Width <= 0 || canvas.Height <= 0 || canvas.Width > MaxSide || canvas.Height > MaxSide || (long)canvas.Width * canvas.Height > MaxPixels
            || source.Width <= 0 || source.Height <= 0 || picture.Width <= 0 || picture.Height <= 0)
        {
            return false;
        }

        var blur = frame.Blur is { } state && state.Samples > 1;
        if (!Fit(canvas.Width, canvas.Height, blur))
            return false;

        var width = canvas.Width;
        var height = canvas.Height;
        var vertices = MeshInto(track, frame, rest, canvas);
        _constants.AsSpan().Clear();
        _constants[0] = width;
        _constants[1] = height;
        _constants[4] = (float)source.Width / picture.Width;
        _constants[5] = (float)source.Height / picture.Height;
        _constants[6] = (float)source.X / picture.Width;
        _constants[7] = (float)source.Y / picture.Height;

        var drawn = _output!;
        if (blur)
        {
            if (!_gpu.Draw(_scene!, width, height, picture, _constants, vertices, blur: false))
                return false;

            var settled = frame.Blur!.Value;
            var current = Shifted(settled.Current, canvas);
            var previous = Shifted(settled.Previous, canvas);
            _constants[2] = settled.Samples;
            _constants[4] = (float)current.X;
            _constants[5] = (float)current.Y;
            _constants[6] = (float)current.Width;
            _constants[7] = (float)current.Height;
            _constants[8] = (float)previous.X;
            _constants[9] = (float)previous.Y;
            _constants[10] = (float)previous.Width;
            _constants[11] = (float)previous.Height;
            Whole(width, height);
            if (!_gpu.Draw(_output!, width, height, _scene!, _constants, _vertices.AsSpan(0, 6), blur: true))
                return false;
        }
        else if (!_gpu.Draw(_output!, width, height, picture, _constants, vertices, blur: false))
        {
            return false;
        }

        var shown = false;
        return _gpu.Read(drawn, _staging!, width, height, (rows, pitch) => shown = sink(rows, pitch, canvas)) && shown;
    }

    public void Dispose()
    {
        _scene?.Dispose();
        _output?.Dispose();
        _staging?.Dispose();
        _scene = null;
        _output = null;
        _staging = null;
    }

    private static EffectBox Shifted(EffectBox box, Rectangle canvas) => box with { X = box.X - canvas.X, Y = box.Y - canvas.Y };

    // The textures only grow, and a frame uses their top left.
    private bool Fit(int width, int height, bool blur)
    {
        if (_output is null || _output.Width < width || _output.Height < height)
        {
            var w = Math.Max(width, _output?.Width ?? 0);
            var h = Math.Max(height, _output?.Height ?? 0);
            var grownW = Math.Min(MaxSide, w + w / 4);
            var grownH = Math.Min(MaxSide, h + h / 4);
            _output?.Dispose();
            _staging?.Dispose();
            _scene?.Dispose();
            _scene = null;
            _output = _gpu.Create(grownW, grownH, GpuUse.Target);
            _staging = _gpu.Create(grownW, grownH, GpuUse.Readback);
            if (_output is null || _staging is null)
                return false;
        }

        if (blur && (_scene is null || _scene.Width < _output.Width || _scene.Height < _output.Height))
        {
            _scene?.Dispose();
            _scene = _gpu.Create(_output.Width, _output.Height, GpuUse.Target);
        }

        return !blur || _scene is not null;
    }

    private ReadOnlySpan<MeshVertex> MeshInto(EffectTrack track, EffectFrame frame, EffectBox rest, Rectangle canvas)
    {
        if (frame.Wobbling)
        {
            track.Triangles(rest, _triangles);
            if (_vertices.Length < _triangles.Count)
                _vertices = new MeshVertex[_triangles.Count];

            for (var i = 0; i < _triangles.Count; i++)
                _vertices[i] = Local(_triangles[i], canvas);

            return _vertices.AsSpan(0, _triangles.Count);
        }

        var left = (float)(rest.X - canvas.X);
        var top = (float)(rest.Y - canvas.Y);
        var right = (float)(rest.Right - canvas.X);
        var bottom = (float)(rest.Bottom - canvas.Y);
        _vertices[0] = new MeshVertex(left, top, 0, 0);
        _vertices[1] = new MeshVertex(left, bottom, 0, 1);
        _vertices[2] = new MeshVertex(right, top, 1, 0);
        _vertices[3] = new MeshVertex(right, top, 1, 0);
        _vertices[4] = new MeshVertex(left, bottom, 0, 1);
        _vertices[5] = new MeshVertex(right, bottom, 1, 1);
        return _vertices.AsSpan(0, 6);
    }

    private static MeshVertex Local(MeshVertex vertex, Rectangle canvas) => vertex with { X = vertex.X - canvas.X, Y = vertex.Y - canvas.Y };

    // Two triangles over the whole canvas, for the blur pass.
    private void Whole(int width, int height)
    {
        _vertices[0] = new MeshVertex(0, 0, 0, 0);
        _vertices[1] = new MeshVertex(0, height, 0, 1);
        _vertices[2] = new MeshVertex(width, 0, 1, 0);
        _vertices[3] = new MeshVertex(width, 0, 1, 0);
        _vertices[4] = new MeshVertex(0, height, 0, 1);
        _vertices[5] = new MeshVertex(width, height, 1, 1);
    }
}
