namespace Winland.Core;

/// <summary>
/// Snaps a floating rectangle to other windows and to the monitor while it is moved or resized.
/// </summary>
internal static class FloatingSnap
{
    /// <summary>
    /// Snaps a floating Client of <paramref name="layout"/> to the other Clients it shows and to the monitor.
    /// Without a known monitor the work area stands in for it.
    /// </summary>
    public static Rectangle AmongClients(
        Layout layout,
        ClientId self,
        Rectangle proposed,
        SnapEdges edges,
        bool resize,
        Rectangle work,
        Rectangle monitor,
        LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.SnapEnabled)
            return proposed;

        var others = new List<Rectangle>();
        foreach (var client in layout.Clients)
        {
            if (client.Id == self || client.Minimized)
                continue;

            if (layout.TryBounds(client.Window, out var bounds))
                others.Add(bounds);
        }

        var screen = monitor.Width > 0 && monitor.Height > 0 ? monitor : work;
        return Apply(proposed, edges, resize, others, work, screen, options);
    }

    public static Rectangle Apply(
        Rectangle box,
        SnapEdges edges,
        bool resize,
        IReadOnlyList<Rectangle> others,
        Rectangle work,
        Rectangle monitor,
        LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(others);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.SnapEnabled)
            return box;

        var border = Math.Max(0, options.BorderSize);
        var overlap = options.SnapBorderOverlap ? border : 0;
        double startX = box.X - border;
        double endX = box.X + box.Width + border;
        double startY = box.Y - border;
        double endY = box.Y + box.Height + border;
        var snapped = false;

        if (options.SnapWindowGap > 0)
        {
            snapped |= SnapWindows(
                ref startX,
                ref endX,
                ref startY,
                ref endY,
                edges,
                resize,
                others,
                options,
                border);
        }

        if (options.SnapMonitorGap > 0)
        {
            snapped |= SnapMonitor(
                ref startX,
                ref endX,
                ref startY,
                ref endY,
                edges,
                resize,
                work,
                monitor,
                options,
                overlap);
        }

        if (!snapped)
            return box;

        startX += border;
        endX -= border;
        startY += border;
        endY -= border;
        var width = Math.Max(1, Round(endX - startX));
        var height = Math.Max(1, Round(endY - startY));
        return new Rectangle(Round(startX), Round(startY), width, height);
    }

    private static bool SnapWindows(
        ref double startX,
        ref double endX,
        ref double startY,
        ref double endY,
        SnapEdges edges,
        bool resize,
        IReadOnlyList<Rectangle> others,
        LayoutOptions options,
        int border)
    {
        var gap = options.SnapWindowGap;
        var gapsX = options.SnapRespectGaps ? options.GapsIn.Left + options.GapsIn.Right : 0;
        var gapsY = options.SnapRespectGaps ? options.GapsIn.Top + options.GapsIn.Bottom : 0;
        var snapped = false;
        var left = false;
        var right = false;
        var up = false;
        var down = false;

        foreach (var other in others)
        {
            double ox0 = other.X - border;
            double ox1 = other.X + other.Width + border;
            double oy0 = other.Y - border;
            double oy1 = other.Y + other.Height + border;
            double sx0 = ox0 - gapsX;
            double sx1 = ox1 + gapsX;
            double sy0 = oy0 - gapsY;
            double sy1 = oy1 + gapsY;

            if (startY <= sy1 && sy0 <= endY)
            {
                if (edges.Left && Can(startX, sx1, gap))
                {
                    Pull(ref startX, ref endX, sx1, resize);
                    left = true;
                    snapped = true;
                }
                else if (edges.Right && Can(endX, sx0, gap))
                {
                    Pull(ref endX, ref startX, sx0, resize);
                    right = true;
                    snapped = true;
                }
            }

            if (startX <= sx1 && sx0 <= endX)
            {
                if (edges.Top && Can(startY, sy1, gap))
                {
                    Pull(ref startY, ref endY, sy1, resize);
                    up = true;
                    snapped = true;
                }
                else if (edges.Bottom && Can(endY, sy0, gap))
                {
                    Pull(ref endY, ref startY, sy0, resize);
                    down = true;
                    snapped = true;
                }
            }

            if (startX == sx1 || sx0 == endX)
            {
                var innerTop = sy0 + gapsY;
                var innerBottom = sy1 - gapsY;
                if (edges.Top && !up && Can(startY, innerTop, gap))
                {
                    Pull(ref startY, ref endY, innerTop, resize);
                    up = true;
                    snapped = true;
                }
                else if (edges.Bottom && !down && Can(endY, innerBottom, gap))
                {
                    Pull(ref endY, ref startY, innerBottom, resize);
                    down = true;
                    snapped = true;
                }
            }

            if (startY == sy1 || sy0 == endY)
            {
                var innerLeft = sx0 + gapsX;
                var innerRight = sx1 - gapsX;
                if (edges.Left && !left && Can(startX, innerLeft, gap))
                {
                    Pull(ref startX, ref endX, innerLeft, resize);
                    left = true;
                    snapped = true;
                }
                else if (edges.Right && !right && Can(endX, innerRight, gap))
                {
                    Pull(ref endX, ref startX, innerRight, resize);
                    right = true;
                    snapped = true;
                }
            }
        }

        return snapped;
    }

    private static bool SnapMonitor(
        ref double startX,
        ref double endX,
        ref double startY,
        ref double endY,
        SnapEdges edges,
        bool resize,
        Rectangle work,
        Rectangle monitor,
        LayoutOptions options,
        int overlap)
    {
        var frame = options.SnapRespectGaps ? options.GapsOut.Apply(work) : work;
        var screen = monitor.Width > 0 && monitor.Height > 0 ? monitor : work;
        var reservedLeft = Math.Max(0, work.X - screen.X);
        var reservedRight = Math.Max(0, (screen.X + screen.Width) - (work.X + work.Width));
        var reservedTop = Math.Max(0, work.Y - screen.Y);
        var reservedBottom = Math.Max(0, (screen.Y + screen.Height) - (work.Y + work.Height));
        var gap = options.SnapMonitorGap;
        var snapped = false;
        double left = frame.X;
        double right = frame.X + frame.Width;
        double top = frame.Y;
        double bottom = frame.Y + frame.Height;

        if (edges.Left && SnapLine(startX, left, -(reservedLeft + overlap), reservedLeft > 0, gap, out var leftTarget))
        {
            Pull(ref startX, ref endX, leftTarget, resize);
            snapped = true;
        }

        if (edges.Right && SnapLine(endX, right, reservedRight + overlap, reservedRight > 0, gap, out var rightTarget))
        {
            Pull(ref endX, ref startX, rightTarget, resize);
            snapped = true;
        }

        if (edges.Top && SnapLine(startY, top, -(reservedTop + overlap), reservedTop > 0, gap, out var topTarget))
        {
            Pull(ref startY, ref endY, topTarget, resize);
            snapped = true;
        }

        if (edges.Bottom && SnapLine(endY, bottom, reservedBottom + overlap, reservedBottom > 0, gap, out var bottomTarget))
        {
            Pull(ref endY, ref startY, bottomTarget, resize);
            snapped = true;
        }

        return snapped;
    }

    // A side snaps to the work area's edge when something is reserved there, and otherwise to the
    // monitor's edge, which lies outward by the reserved space and the border overlap.
    private static bool SnapLine(double side, double edge, int outward, bool reserved, int gap, out double target)
    {
        if (reserved && Can(side, edge, gap))
        {
            target = edge;
            return true;
        }

        target = edge + outward;
        return Can(side, target, gap);
    }

    private static bool Can(double side, double other, int gap) => Math.Abs(side - other) < gap;

    private static void Pull(ref double start, ref double end, double target, bool resize)
    {
        if (resize)
        {
            start = target;
            return;
        }

        end = target + (end - start);
        start = target;
    }

    private static int Round(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
