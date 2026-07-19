using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using IconCreator.Model;

namespace IconCreator.IO;

/// <summary>
/// Writes a Windows .ICO containing multiple resolutions.
/// 256px slices are stored PNG-compressed; smaller slices use a 32-bit BGRA DIB
/// with a 1-bit AND mask, matching what Windows Explorer expects.
/// </summary>
public static class IcoEncoder
{
    public static void Save(string path, IReadOnlyList<IconSlice> slices)
    {
        using var fs = File.Create(path);
        Write(fs, slices);
    }

    public static void Write(Stream stream, IReadOnlyList<IconSlice> slices)
    {
        var included = slices.Where(s => s.IncludeInExport)
                             .OrderBy(s => s.Size)
                             .ToList();
        if (included.Count == 0)
            throw new InvalidOperationException("No sizes selected for export.");

        var images = included.Select(EncodeSlice).ToList();

        using var bw = new BinaryWriter(stream);

        // ICONDIR
        bw.Write((ushort)0);                 // reserved
        bw.Write((ushort)1);                 // type = icon
        bw.Write((ushort)images.Count);

        int offset = 6 + 16 * images.Count;  // header + all directory entries
        for (int i = 0; i < images.Count; i++)
        {
            var (slice, data, bpp) = (included[i], images[i].data, images[i].bpp);
            bw.Write((byte)(slice.Size >= 256 ? 0 : slice.Size)); // width  (0 => 256)
            bw.Write((byte)(slice.Size >= 256 ? 0 : slice.Size)); // height (0 => 256)
            bw.Write((byte)0);               // color count (0 for true colour)
            bw.Write((byte)0);               // reserved
            bw.Write((ushort)1);             // planes
            bw.Write((ushort)bpp);           // bits per pixel
            bw.Write((uint)data.Length);     // bytes in resource
            bw.Write((uint)offset);          // offset of data
            offset += data.Length;
        }

        foreach (var img in images)
            bw.Write(img.data);
    }

    private static (byte[] data, int bpp) EncodeSlice(IconSlice slice)
    {
        return slice.Size >= 256
            ? (EncodePng(slice.Buffer), 32)
            : (EncodeDib(slice.Buffer), 32);
    }

    private static byte[] EncodePng(PixelBuffer buffer)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(buffer.ToBitmapSource()));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static byte[] EncodeDib(PixelBuffer buffer)
    {
        int w = buffer.Width, h = buffer.Height;
        int xorStride = w * 4;
        int andStride = ((w + 31) / 32) * 4;   // 1bpp rows padded to 32 bits

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER — biHeight is doubled to cover XOR + AND masks.
        bw.Write(40);                  // biSize
        bw.Write(w);                   // biWidth
        bw.Write(h * 2);               // biHeight
        bw.Write((ushort)1);           // biPlanes
        bw.Write((ushort)32);          // biBitCount
        bw.Write(0);                   // biCompression = BI_RGB
        bw.Write(xorStride * h + andStride * h); // biSizeImage
        bw.Write(0); bw.Write(0);      // resolution
        bw.Write(0); bw.Write(0);      // palette

        // XOR bitmap: 32-bit BGRA, bottom-up.
        for (int y = h - 1; y >= 0; y--)
            for (int x = 0; x < w; x++)
            {
                int argb = buffer.Get(x, y);
                byte a = (byte)(argb >> 24), r = (byte)(argb >> 16),
                     g = (byte)(argb >> 8),  b = (byte)argb;
                bw.Write(b); bw.Write(g); bw.Write(r); bw.Write(a);
            }

        // AND mask: 1 = transparent. Alpha channel already handles blending,
        // but a correct mask keeps legacy renderers happy.
        byte[] row = new byte[andStride];
        for (int y = h - 1; y >= 0; y--)
        {
            Array.Clear(row, 0, row.Length);
            for (int x = 0; x < w; x++)
            {
                int a = (buffer.Get(x, y) >> 24) & 0xFF;
                if (a == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
            }
            bw.Write(row);
        }

        return ms.ToArray();
    }
}
