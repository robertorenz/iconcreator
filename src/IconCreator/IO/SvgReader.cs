using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml.Linq;
using IconCreator.Vector;

namespace IconCreator.IO;

/// <summary>
/// Parses the common SVG shape elements (rect, circle, ellipse, line, polyline,
/// polygon, text) back into editable <see cref="VShape"/>s, mapping the document's
/// viewBox and any group/element transforms onto a <paramref name="board"/>² canvas.
/// Unsupported constructs (e.g. &lt;path&gt;) are skipped.
/// </summary>
public static class SvgReader
{
    private readonly record struct Affine(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

        public Point Map(double x, double y) => new(A * x + C * y + E, B * x + D * y + F);
        public double ScaleX => Math.Sqrt(A * A + B * B);
        public double ScaleY => Math.Sqrt(C * C + D * D);

        // this ∘ other  (apply other first, then this)
        public Affine Compose(Affine o) => new(
            A * o.A + C * o.B, B * o.A + D * o.B,
            A * o.C + C * o.D, B * o.C + D * o.D,
            A * o.E + C * o.F + E, B * o.E + D * o.F + F);
    }

    public static List<VShape> Read(string path, int board)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new InvalidOperationException("Empty SVG.");

        Affine start = ViewBoxTransform(root, board);
        var shapes = new List<VShape>();
        Walk(root, start, shapes);
        return shapes;
    }

    private static Affine ViewBoxTransform(XElement root, int board)
    {
        var vb = (string?)root.Attribute("viewBox");
        double minX = 0, minY = 0, w, h;
        if (!string.IsNullOrWhiteSpace(vb))
        {
            var p = vb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 4)
            {
                minX = D(p[0]); minY = D(p[1]); w = D(p[2]); h = D(p[3]);
            }
            else { w = h = board; }
        }
        else
        {
            w = D((string?)root.Attribute("width") ?? board.ToString());
            h = D((string?)root.Attribute("height") ?? board.ToString());
            if (w <= 0) w = board;
            if (h <= 0) h = board;
        }

        double scale = Math.Min(board / w, board / h);   // uniform, "meet"
        double ox = (board - w * scale) / 2 - minX * scale;
        double oy = (board - h * scale) / 2 - minY * scale;
        return new Affine(scale, 0, 0, scale, ox, oy);
    }

    private static void Walk(XElement el, Affine ctm, List<VShape> outp)
    {
        foreach (var child in el.Elements())
        {
            var m = ctm.Compose(ParseTransform((string?)child.Attribute("transform")));
            switch (child.Name.LocalName)
            {
                case "g": Walk(child, m, outp); break;
                case "rect": AddRect(child, m, outp); break;
                case "circle": AddCircle(child, m, outp); break;
                case "ellipse": AddEllipse(child, m, outp); break;
                case "line": AddLine(child, m, outp); break;
                case "polyline": AddPoly(child, m, outp, false); break;
                case "polygon": AddPoly(child, m, outp, true); break;
                case "text": AddText(child, m, outp); break;
                case "image": AddImage(child, m, outp); break;
                default: /* path & others: skip */ break;
            }
        }
    }

    // ----------------------------------------------------------- elements

    private static void AddRect(XElement e, Affine m, List<VShape> o)
    {
        double x = A(e, "x"), y = A(e, "y"), w = A(e, "width"), h = A(e, "height");
        var tl = m.Map(x, y);
        var r = new Rectangle
        {
            Width = Math.Max(1, w * m.ScaleX),
            Height = Math.Max(1, h * m.ScaleY),
            RadiusX = A(e, "rx") * m.ScaleX,
            RadiusY = A(e, "ry") * m.ScaleY
        };
        StyleShape(r, e, m);
        Canvas.SetLeft(r, tl.X);
        Canvas.SetTop(r, tl.Y);
        o.Add(new VShape(VKind.Rectangle, r));
    }

    private static void AddCircle(XElement e, Affine m, List<VShape> o)
    {
        double cx = A(e, "cx"), cy = A(e, "cy"), r = A(e, "r");
        AddEllipseCore(e, m, o, cx, cy, r, r);
    }

    private static void AddEllipse(XElement e, Affine m, List<VShape> o)
        => AddEllipseCore(e, m, o, A(e, "cx"), A(e, "cy"), A(e, "rx"), A(e, "ry"));

    private static void AddEllipseCore(XElement e, Affine m, List<VShape> o, double cx, double cy, double rx, double ry)
    {
        var tl = m.Map(cx - rx, cy - ry);
        var el = new Ellipse { Width = Math.Max(1, 2 * rx * m.ScaleX), Height = Math.Max(1, 2 * ry * m.ScaleY) };
        StyleShape(el, e, m);
        Canvas.SetLeft(el, tl.X);
        Canvas.SetTop(el, tl.Y);
        o.Add(new VShape(VKind.Ellipse, el));
    }

    private static void AddLine(XElement e, Affine m, List<VShape> o)
    {
        var p1 = m.Map(A(e, "x1"), A(e, "y1"));
        var p2 = m.Map(A(e, "x2"), A(e, "y2"));
        var l = new Line
        {
            X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,
            Stroke = Paint(e, "stroke", Brushes.Black),
            StrokeThickness = Math.Max(0.5, StrokeW(e) * m.ScaleX),
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        o.Add(new VShape(VKind.Line, l));
    }

    private static void AddPoly(XElement e, Affine m, List<VShape> o, bool close)
    {
        var raw = ((string?)e.Attribute("points") ?? "").Split(new[] { ' ', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var pts = new PointCollection();
        for (int i = 0; i + 1 < raw.Length; i += 2)
            pts.Add(m.Map(D(raw[i]), D(raw[i + 1])));
        if (pts.Count < 2) return;
        if (close && pts.Count > 2) pts.Add(pts[0]);

        var pl = new Polyline
        {
            Points = pts,
            Fill = Paint(e, "fill", Brushes.Transparent),
            Stroke = Paint(e, "stroke", Brushes.Black),
            StrokeThickness = Math.Max(0.5, StrokeW(e) * m.ScaleX),
            StrokeLineJoin = PenLineJoin.Round
        };
        o.Add(new VShape(VKind.Path, pl));
    }

    private static void AddText(XElement e, Affine m, List<VShape> o)
    {
        string text = e.Value.Trim();
        if (text.Length == 0) return;
        double fs = A(e, "font-size", 16) * m.ScaleY;
        var pos = m.Map(A(e, "x"), A(e, "y"));
        var tb = new TextBlock
        {
            Text = text,
            FontSize = Math.Max(1, fs),
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = Paint(e, "fill", Brushes.Black)
        };
        Canvas.SetLeft(tb, pos.X);
        Canvas.SetTop(tb, pos.Y - fs * 0.8);   // SVG y is the baseline
        o.Add(new VShape(VKind.Text, tb));
    }

    private static void AddImage(XElement e, Affine m, List<VShape> o)
    {
        XNamespace xlink = "http://www.w3.org/1999/xlink";
        string? href = (string?)e.Attribute(xlink + "href") ?? (string?)e.Attribute("href");
        if (href == null) return;
        int comma = href.IndexOf(',');
        if (!href.StartsWith("data:") || comma < 0) return;   // only embedded data URIs
        BitmapImage bmp;
        try
        {
            var bytes = Convert.FromBase64String(href[(comma + 1)..]);
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new System.IO.MemoryStream(bytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch { return; }

        double x = A(e, "x"), y = A(e, "y"), w = A(e, "width"), h = A(e, "height");
        var tl = m.Map(x, y);
        var img = new Image { Source = bmp, Stretch = System.Windows.Media.Stretch.Fill,
            Width = Math.Max(1, w * m.ScaleX), Height = Math.Max(1, h * m.ScaleY) };
        Canvas.SetLeft(img, tl.X);
        Canvas.SetTop(img, tl.Y);
        o.Add(new VShape(VKind.Image, img));
    }

    // ----------------------------------------------------------- helpers

    private static void StyleShape(Shape s, XElement e, Affine m)
    {
        s.Fill = Paint(e, "fill", Brushes.Black);
        s.Stroke = Paint(e, "stroke", Brushes.Transparent);
        s.StrokeThickness = StrokeW(e) * m.ScaleX;
    }

    private static double StrokeW(XElement e) => A(e, "stroke-width", 1);

    /// <summary>Resolve a paint attribute (checks presence in a style="" block too).</summary>
    private static Brush Paint(XElement e, string name, Brush fallback)
    {
        string? v = Attr(e, name);
        if (v == null) return fallback;
        v = v.Trim();
        if (v.Equals("none", StringComparison.OrdinalIgnoreCase) || v.StartsWith("url", StringComparison.OrdinalIgnoreCase))
            return Brushes.Transparent;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(v)!;
            double op = ParseOpacity(e, name);
            if (op < 1) c.A = (byte)Math.Clamp(c.A * op, 0, 255);
            return new SolidColorBrush(c);
        }
        catch { return fallback; }
    }

    private static double ParseOpacity(XElement e, string name)
    {
        double o = 1;
        var overall = Attr(e, "opacity");
        if (overall != null && double.TryParse(overall, NumberStyles.Float, CultureInfo.InvariantCulture, out var ov)) o *= ov;
        var specific = Attr(e, name + "-opacity");
        if (specific != null && double.TryParse(specific, NumberStyles.Float, CultureInfo.InvariantCulture, out var sv)) o *= sv;
        return o;
    }

    /// <summary>Attribute value, falling back to a declaration inside a style="" attribute.</summary>
    private static string? Attr(XElement e, string name)
    {
        var a = (string?)e.Attribute(name);
        if (a != null) return a;
        var style = (string?)e.Attribute("style");
        if (style == null) return null;
        foreach (var decl in style.Split(';'))
        {
            var kv = decl.Split(':', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return null;
    }

    private static double A(XElement e, string name, double dflt = 0)
    {
        var v = Attr(e, name);
        return v != null && double.TryParse(TrimUnits(v), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : dflt;
    }

    private static string TrimUnits(string v) => v.TrimEnd('p', 'x', 't', 'e', 'm', '%', ' ');

    private static double D(string v) => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static Affine ParseTransform(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return Affine.Identity;
        var result = Affine.Identity;
        int i = 0;
        while (i < t.Length)
        {
            int open = t.IndexOf('(', i);
            if (open < 0) break;
            string fn = t.Substring(i, open - i).Trim();
            int close = t.IndexOf(')', open);
            if (close < 0) break;
            var args = t.Substring(open + 1, close - open - 1)
                        .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(D).ToArray();
            result = result.Compose(fn switch
            {
                "translate" => new Affine(1, 0, 0, 1, args.ElementAtOrDefault(0), args.ElementAtOrDefault(1)),
                "scale" => new Affine(args.ElementAtOrDefault(0), 0, 0, args.Length > 1 ? args[1] : args.ElementAtOrDefault(0), 0, 0),
                "matrix" when args.Length == 6 => new Affine(args[0], args[1], args[2], args[3], args[4], args[5]),
                _ => Affine.Identity
            });
            i = close + 1;
        }
        return result;
    }
}
