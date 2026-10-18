namespace Winland;

/// <summary>The part of a Monitor a cursor zoom shows: its top-left in desktop pixels, and the factor it is shown at.</summary>
public readonly record struct ZoomView(double Factor, double X, double Y);

/// <summary>
/// Where the cursor zoom looks, as the pinned release's zoom controller works it out. Only the Monitor under the
/// cursor is zoomed. With <c>cursor:zoom_detached_camera</c> the view keeps the point under the cursor in place when the
/// factor changes, and pans only when the cursor leaves the middle 90% of it, or never with <c>cursor:zoom_rigid</c>.
/// Without it the view zooms about the cursor, or keeps the cursor in its middle when rigid. The view never leaves the
/// Monitor.
/// </summary>
public sealed class ZoomCamera
{
    private const double Padding = 0.9;

    private readonly Dictionary<Rectangle, View> _views = [];

    /// <summary>The view for the cursor at <paramref name="pointer"/> on <paramref name="monitor"/>, or null when nothing is zoomed.</summary>
    public ZoomView? Follow(Rectangle monitor, Point pointer, CursorZoom zoom)
    {
        ArgumentNullException.ThrowIfNull(zoom);

        // A Monitor shown unzoomed starts its view over at its next change of factor, as in the pinned release.
        foreach (var (bounds, other) in _views)
        {
            if (bounds != monitor || zoom.Factor <= 1)
                other.Reset = true;
        }

        if (zoom.Factor <= 1 || monitor.Width <= 0 || monitor.Height <= 0)
            return null;

        if (!_views.TryGetValue(monitor, out var view))
        {
            view = new View();
            _views[monitor] = view;
        }

        var factor = zoom.Factor;
        double mouseX = pointer.X - monitor.X;
        double mouseY = pointer.Y - monitor.Y;
        double left;
        double top;
        if (zoom.DetachedCamera)
        {
            view.Follow(monitor, mouseX, mouseY, factor, zoom.Rigid);
            left = view.X;
            top = view.Y;
        }
        else
        {
            var anchorX = zoom.Rigid ? monitor.Width / 2.0 : mouseX;
            var anchorY = zoom.Rigid ? monitor.Height / 2.0 : mouseY;
            left = mouseX - (anchorX / factor);
            top = mouseY - (anchorY / factor);
        }

        left = Math.Clamp(left, 0, monitor.Width - (monitor.Width / factor));
        top = Math.Clamp(top, 0, monitor.Height - (monitor.Height / factor));
        return new ZoomView(factor, monitor.X + left, monitor.Y + top);
    }

    private sealed class View
    {
        private double _lastFactor = 1;
        private bool _padEdges = true;
        private double _width;
        private double _height;

        public bool Reset { get; set; } = true;

        public double X { get; private set; }

        public double Y { get; private set; }

        public void Follow(Rectangle monitor, double mouseX, double mouseY, double factor, bool rigid)
        {
            if (_lastFactor != factor)
            {
                if (Reset)
                {
                    Reset = false;
                    (X, Y, _width, _height) = (0, 0, monitor.Width, monitor.Height);
                }

                // The desktop point under the cursor stays under it at the new factor.
                var unitX = mouseX / monitor.Width;
                var unitY = mouseY / monitor.Height;
                var worldX = X + (unitX * _width);
                var worldY = Y + (unitY * _height);
                _width = monitor.Width / factor;
                _height = monitor.Height / factor;
                X = worldX - (unitX * _width);
                Y = worldY - (unitY * _height);

                // A step that leaves the cursor outside the padded view would jump to it, so the padding waits until the
                // cursor is back inside.
                if (!Contains(Padded(), mouseX, mouseY))
                    _padEdges = false;

                _lastFactor = factor;
            }

            if (!_padEdges && Contains(Padded(), mouseX, mouseY))
                _padEdges = true;

            (double X, double Y, double Width, double Height) box = _padEdges ? Padded() : (X, Y, _width, _height);
            if (rigid || Contains(box, mouseX, mouseY))
                return;

            if (mouseX < box.X)
                X -= box.X - mouseX;
            if (mouseY < box.Y)
                Y -= box.Y - mouseY;
            if (mouseY > box.Y + box.Height)
                Y += mouseY - (box.Y + box.Height);
            if (mouseX > box.X + box.Width)
                X += mouseX - (box.X + box.Width);
        }

        private (double X, double Y, double Width, double Height) Padded()
        {
            var width = _width * Padding;
            var height = _height * Padding;
            return (X + ((_width - width) / 2), Y + ((_height - height) / 2), width, height);
        }

        private static bool Contains((double X, double Y, double Width, double Height) box, double x, double y) =>
            x >= box.X && y >= box.Y && x < box.X + box.Width && y < box.Y + box.Height;
    }
}
