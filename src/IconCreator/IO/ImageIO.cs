using System.IO;
using System.Windows.Media.Imaging;
using IconCreator.Model;

namespace IconCreator.IO;

/// <summary>Loading of external images (.png/.jpg/.bmp/.ico) and PNG export.</summary>
public static class ImageIO
{
    public const string OpenFilter =
        "Images (*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.gif|All files (*.*)|*.*";

    /// <summary>Load every frame from a file (an .ico can carry many).</summary>
    public static IReadOnlyList<BitmapSource> LoadFrames(string path)
    {
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

    public static void ExportPng(string path, PixelBuffer buffer)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(buffer.ToBitmapSource()));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
