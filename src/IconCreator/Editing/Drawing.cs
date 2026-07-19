using IconCreator.Model;

namespace IconCreator.Editing;

public enum ToolKind { Pencil, Eraser, Fill, Eyedropper, Line, Rectangle, Ellipse, RectangleFilled, EllipseFilled }

/// <summary>Stateless raster primitives that operate directly on a <see cref="PixelBuffer"/>.</summary>
public static class Drawing
{
    /// <summary>Stamp a square brush centred on (cx,cy). size 1 = single pixel.</summary>
    public static void Stamp(PixelBuffer buf, int cx, int cy, int argb, int size, bool blend)
    {
        int half = size / 2;
        for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
            {
                int x = cx - half + dx, y = cy - half + dy;
                if (blend) buf.Blend(x, y, argb);
                else buf.Set(x, y, argb);
            }
    }

    public static void Line(PixelBuffer buf, int x0, int y0, int x1, int y1, int argb, int size, bool blend)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Stamp(buf, x0, y0, argb, size, blend);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public static void Rectangle(PixelBuffer buf, int x0, int y0, int x1, int y1, int argb, int size, bool filled, bool blend)
    {
        int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
        int minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);

        if (filled)
        {
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (blend) buf.Blend(x, y, argb); else buf.Set(x, y, argb);
            return;
        }

        Line(buf, minX, minY, maxX, minY, argb, size, blend);
        Line(buf, minX, maxY, maxX, maxY, argb, size, blend);
        Line(buf, minX, minY, minX, maxY, argb, size, blend);
        Line(buf, maxX, minY, maxX, maxY, argb, size, blend);
    }

    public static void Ellipse(PixelBuffer buf, int x0, int y0, int x1, int y1, int argb, int size, bool filled, bool blend)
    {
        int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
        int minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
        double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0;
        double rx = Math.Max(0.5, (maxX - minX) / 2.0), ry = Math.Max(0.5, (maxY - minY) / 2.0);

        if (filled)
        {
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    double nx = (x - cx) / rx, ny = (y - cy) / ry;
                    if (nx * nx + ny * ny <= 1.0)
                        if (blend) buf.Blend(x, y, argb); else buf.Set(x, y, argb);
                }
            return;
        }

        // Trace the outline by sampling angles densely enough for any size.
        int steps = (int)(Math.Max(rx, ry) * 8) + 16;
        for (int i = 0; i < steps; i++)
        {
            double t = 2 * Math.PI * i / steps;
            int x = (int)Math.Round(cx + rx * Math.Cos(t));
            int y = (int)Math.Round(cy + ry * Math.Sin(t));
            Stamp(buf, x, y, argb, size, blend);
        }
    }

    /// <summary>Scanline flood fill starting at (sx,sy).</summary>
    public static void Fill(PixelBuffer buf, int sx, int sy, int argb, int tolerance)
    {
        if (!buf.InBounds(sx, sy)) return;
        int target = buf.Get(sx, sy);
        if (target == argb && tolerance == 0) return;

        int w = buf.Width, h = buf.Height;
        var visited = new bool[w * h];
        var stack = new Stack<(int x, int y)>();
        stack.Push((sx, sy));

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || y < 0 || x >= w || y >= h) continue;
            int idx = y * w + x;
            if (visited[idx]) continue;
            if (!ColorClose(buf.Pixels[idx], target, tolerance)) continue;

            visited[idx] = true;
            buf.Pixels[idx] = argb;

            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }
    }

    private static bool ColorClose(int a, int b, int tol)
    {
        if (tol == 0) return a == b;
        int da = Math.Abs(((a >> 24) & 0xFF) - ((b >> 24) & 0xFF));
        int dr = Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF));
        int dg = Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF));
        int db = Math.Abs((a & 0xFF) - (b & 0xFF));
        return da <= tol && dr <= tol && dg <= tol && db <= tol;
    }
}
