using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconCreator.Model;

/// <summary>
/// A straight-alpha ARGB pixel buffer (0xAARRGGBB) backing a WPF <see cref="WriteableBitmap"/>.
/// Little-endian int layout maps directly onto <see cref="PixelFormats.Bgra32"/>.
/// </summary>
public sealed class PixelBuffer
{
    public int Width { get; }
    public int Height { get; }
    public int[] Pixels { get; }

    private readonly WriteableBitmap _bitmap;
    public WriteableBitmap Bitmap => _bitmap;

    public PixelBuffer(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new int[width * height];
        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public int Get(int x, int y) => InBounds(x, y) ? Pixels[y * Width + x] : 0;

    public void Set(int x, int y, int argb)
    {
        if (InBounds(x, y)) Pixels[y * Width + x] = argb;
    }

    /// <summary>Alpha-composite <paramref name="argb"/> over the existing pixel.</summary>
    public void Blend(int x, int y, int argb)
    {
        if (!InBounds(x, y)) return;
        int sa = (argb >> 24) & 0xFF;
        if (sa == 0) return;
        if (sa == 255) { Pixels[y * Width + x] = argb; return; }

        int dst = Pixels[y * Width + x];
        int da = (dst >> 24) & 0xFF;

        int sr = (argb >> 16) & 0xFF, sg = (argb >> 8) & 0xFF, sb = argb & 0xFF;
        int dr = (dst >> 16) & 0xFF, dg = (dst >> 8) & 0xFF, db = dst & 0xFF;

        int outA = sa + da * (255 - sa) / 255;
        if (outA == 0) { Pixels[y * Width + x] = 0; return; }

        int outR = (sr * sa + dr * da * (255 - sa) / 255) / outA;
        int outG = (sg * sa + dg * da * (255 - sa) / 255) / outA;
        int outB = (sb * sa + db * da * (255 - sa) / 255) / outA;

        Pixels[y * Width + x] = (outA << 24) | (outR << 16) | (outG << 8) | outB;
    }

    public void Clear()
    {
        Array.Clear(Pixels, 0, Pixels.Length);
    }

    /// <summary>Push the CPU pixel array to the on-screen bitmap.</summary>
    public void Flush()
    {
        _bitmap.WritePixels(new Int32Rect(0, 0, Width, Height), Pixels, Width * 4, 0);
    }

    public int[] Snapshot() => (int[])Pixels.Clone();

    public void Restore(int[] snapshot)
    {
        Array.Copy(snapshot, Pixels, Pixels.Length);
        Flush();
    }

    /// <summary>Copy an existing (already BGRA) bitmap source into this buffer, scaling as needed.</summary>
    public void LoadFrom(BitmapSource src)
    {
        BitmapSource s = src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

        if (s.PixelWidth != Width || s.PixelHeight != Height)
        {
            var scaled = new TransformedBitmap(s, new ScaleTransform(
                (double)Width / s.PixelWidth, (double)Height / s.PixelHeight));
            s = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        }

        var buf = new int[Width * Height];
        s.CopyPixels(new Int32Rect(0, 0, Width, Height), buf, Width * 4, 0);
        Array.Copy(buf, Pixels, buf.Length);
        Flush();
    }

    /// <summary>Return an immutable BGRA source snapshot (for encoding / preview).</summary>
    public BitmapSource ToBitmapSource()
    {
        var bmp = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null,
            Pixels, Width * 4);
        bmp.Freeze();
        return bmp;
    }
}
