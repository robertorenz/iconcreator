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

            // Vector authoring round-trip: build shapes -> SVG -> rasterise.
            var shapes = new List<Vector.VShape>();
            var rect = new System.Windows.Shapes.Rectangle
            { Width = 180, Height = 180, RadiusX = 24, RadiusY = 24, Fill = System.Windows.Media.Brushes.SteelBlue };
            System.Windows.Controls.Canvas.SetLeft(rect, 38);
            System.Windows.Controls.Canvas.SetTop(rect, 38);
            shapes.Add(new Vector.VShape(Vector.VKind.Rectangle, rect));

            var line = new System.Windows.Shapes.Line
            { X1 = 60, Y1 = 60, X2 = 196, Y2 = 196, Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 8 };
            shapes.Add(new Vector.VShape(Vector.VKind.Line, line));

            string authored = SvgWriter.Build(256, shapes);
            string outSvg = Path.Combine(Path.GetTempPath(), "iconcreator_authored.svg");
            File.WriteAllText(outSvg, authored);
            var rt = ImageIO.RasterizeSvg(outSvg, 64);
            var vb = new int[64 * 64];
            rt.CopyPixels(vb, 64 * 4, 0);
            int vOpaque = vb.Count(px => ((px >> 24) & 0xFF) > 10);
            report.AppendLine($"authoredSvg has <rect>={authored.Contains("<rect")} <line>={authored.Contains("<line")} rasterOpaque={vOpaque}");

            // SVG read-back: parse the authored file into editable shapes.
            var reparsed = SvgReader.Read(outSvg, 256);
            report.AppendLine($"svgReadBack shapes={reparsed.Count} kinds={string.Join(",", reparsed.Select(s => s.Kind))}");

            // Vector image element round-trip: <image> with embedded PNG.
            var pix = new int[8 * 8];
            for (int i = 0; i < pix.Length; i++) pix[i] = unchecked((int)0xFFFF0000);
            var bs = System.Windows.Media.Imaging.BitmapSource.Create(8, 8, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, pix, 8 * 4);
            var imgEl = new System.Windows.Controls.Image { Source = bs, Width = 100, Height = 100 };
            System.Windows.Controls.Canvas.SetLeft(imgEl, 50);
            System.Windows.Controls.Canvas.SetTop(imgEl, 50);
            string imgSvg = SvgWriter.Build(256, new[] { new Vector.VShape(Vector.VKind.Image, imgEl) });
            string imgPath = Path.Combine(Path.GetTempPath(), "iconcreator_image.svg");
            File.WriteAllText(imgPath, imgSvg);
            var imgBack = SvgReader.Read(imgPath, 256);
            report.AppendLine($"imageSvg has <image>={imgSvg.Contains("<image")} dataUri={imgSvg.Contains("data:image/png;base64,")} readBack={imgBack.Count} kind={(imgBack.Count > 0 ? imgBack[0].Kind.ToString() : "none")}");
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
