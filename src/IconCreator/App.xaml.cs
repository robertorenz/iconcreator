using System.IO;
using System.Windows;
using System.Windows.Threading;
using IconCreator.IO;
using IconCreator.Model;

namespace IconCreator;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest"))
        {
            RunSelfTest();
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    /// <summary>Round-trips a synthetic icon through the real encoder + loader and writes a report.</summary>
    private static void RunSelfTest()
    {
        var report = new System.Text.StringBuilder();
        try
        {
            var doc = new IconDocument(new[] { 16, 32, 48, 256 });
            foreach (var slice in doc.Slices)
            {
                // Fill with an opaque diagonal + a transparent corner to exercise alpha.
                for (int y = 0; y < slice.Size; y++)
                    for (int x = 0; x < slice.Size; x++)
                    {
                        int a = (x + y) < slice.Size / 2 ? 0 : 255;
                        slice.Buffer.Set(x, y, (a << 24) | (0x2F << 16) | (0x81 << 8) | 0xD6);
                    }
            }

            string path = Path.Combine(Path.GetTempPath(), "iconcreator_selftest.ico");
            IcoEncoder.Save(path, doc.Slices);
            long bytes = new FileInfo(path).Length;

            var frames = ImageIO.LoadFrames(path);
            var sizes = frames.Select(f => f.PixelWidth).OrderBy(s => s).ToArray();

            report.AppendLine("SELFTEST OK");
            report.AppendLine($"file={path}");
            report.AppendLine($"bytes={bytes}");
            report.AppendLine($"frames={frames.Count}");
            report.AppendLine($"sizes={string.Join(",", sizes)}");

            // SVG rasterisation check (if a sample is present).
            string svg = Path.Combine(Path.GetTempPath(), "sample_check.svg");
            if (File.Exists(svg))
            {
                var raster = ImageIO.RasterizeSvg(svg, 128);
                var buf = new int[128 * 128];
                raster.CopyPixels(buf, 128 * 4, 0);
                int opaque = buf.Count(px => ((px >> 24) & 0xFF) > 10);
                report.AppendLine($"svg={raster.PixelWidth}x{raster.PixelHeight} opaquePixels={opaque}");
            }
        }
        catch (Exception ex)
        {
            report.AppendLine("SELFTEST FAIL");
            report.AppendLine(ex.ToString());
        }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "iconcreator_selftest.txt"), report.ToString());
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Views.ModalDialog.Error(
            MainWindow,
            Localization.Loc.T("err.unexpectedTitle"),
            e.Exception.Message);
        e.Handled = true;
    }
}
