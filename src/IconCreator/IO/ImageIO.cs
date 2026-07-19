using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IconCreator.Model;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace IconCreator.IO;

/// <summary>Loading of external images (.png/.jpg/.bmp/.ico/.gif/.svg) and PNG export.</summary>
public static class ImageIO
{
    public const string OpenFilter =
        "Images (*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.gif;*.svg)|*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.gif;*.svg|All files (*.*)|*.*";

    /// <summary>Load every frame from a file (an .ico can carry many; an .svg is rasterised).</summary>
    public static IReadOnlyList<BitmapSource> LoadFrames(string path)
    {
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return new[] { RasterizeSvg(path, 512) };

        using var fs = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(fs,
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var list = new List<BitmapSource>();
        foreach (var frame in decoder.Frames)
        {
            var bs = frame as BitmapSource;
            bs?.Freeze();
            if (bs != null) list.Add(bs);
        }
        return list;
    }

    /// <summary>Render an SVG to a square, high-resolution bitmap (aspect preserved, centred).</summary>
    public static BitmapSource RasterizeSvg(string path, int size)
    {
        var settings = new WpfDrawingSettings
        {
            IncludeRuntime = false,
            TextAsGeometry = true
        };

        using var reader = new FileSvgReader(settings);
        Drawing drawing = reader.Read(path)
            ?? throw new InvalidOperationException("The SVG could not be parsed.");

        Rect bounds = drawing.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("The SVG has no drawable content.");

        double scale = Math.Min(size / bounds.Width, size / bounds.Height);
        double w = bounds.Width * scale, h = bounds.Height * scale;
        double ox = (size - w) / 2, oy = (size - h) / 2;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(ox, oy));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            dc.DrawDrawing(drawing);
            dc.Pop(); dc.Pop(); dc.Pop();
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>Pick the frame whose dimensions best fit a target size.</summary>
    public static BitmapSource BestFrameFor(IReadOnlyList<BitmapSource> frames, int size)
    {
        // Prefer an exact match, otherwise the smallest frame that is >= target,
        // otherwise the largest available.
        var exact = frames.FirstOrDefault(f => f.PixelWidth == size);
        if (exact != null) return exact;

        var larger = frames.Where(f => f.PixelWidth >= size)
                           .OrderBy(f => f.PixelWidth).FirstOrDefault();
        if (larger != null) return larger;

        return frames.OrderByDescending(f => f.PixelWidth).First();
    }

    /// <summary>
    /// Render <paramref name="img"/> at rectangle (x,y,w,h) — expressed in the target
    /// icon's pixel space — onto a transparent <paramref name="size"/>² canvas and return
    /// it as straight-alpha ARGB pixels ready to composite into a slice.
    /// </summary>
    public static int[] RasterizePlacement(BitmapSource img, double x, double y, double w, double h, int size)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(img, new System.Windows.Rect(x, y, w, h));

        var rtb = new RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(visual);

        var straight = new FormatConvertedBitmap(rtb, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var buf = new int[size * size];
        straight.CopyPixels(buf, size * 4, 0);
        return buf;
    }

    public static void ExportPng(string path, PixelBuffer buffer)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(buffer.ToBitmapSource()));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
