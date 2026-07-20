using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IconCreator.Vector;

public enum VKind { Rectangle, Ellipse, Line, Path, Text }

/// <summary>
/// A single authored vector object. Wraps the live WPF element used for editing
/// and knows how to report/transform its bounds and serialise itself to SVG.
/// </summary>
public sealed class VShape
{
    public VKind Kind { get; }
    public FrameworkElement Element { get; }

    public VShape(VKind kind, FrameworkElement element)
    {
        Kind = kind;
        Element = element;
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- bounds

    public Rect Bounds
    {
        get
        {
            switch (Kind)
            {
                case VKind.Line:
                {
                    var l = (Line)Element;
                    double x = Math.Min(l.X1, l.X2), y = Math.Min(l.Y1, l.Y2);
                    return new Rect(x, y, Math.Abs(l.X2 - l.X1), Math.Abs(l.Y2 - l.Y1));
                }
                case VKind.Path:
                {
                    var p = (Polyline)Element;
                    if (p.Points.Count == 0) return Rect.Empty;
                    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                    foreach (var pt in p.Points)
                    {
                        minX = Math.Min(minX, pt.X); minY = Math.Min(minY, pt.Y);
                        maxX = Math.Max(maxX, pt.X); maxY = Math.Max(maxY, pt.Y);
                    }
                    return new Rect(minX, minY, maxX - minX, maxY - minY);
                }
                case VKind.Text:
                {
                    var t = (TextBlock)Element;
                    double w = t.ActualWidth > 0 ? t.ActualWidth : t.DesiredSize.Width;
                    double h = t.ActualHeight > 0 ? t.ActualHeight : t.DesiredSize.Height;
                    return new Rect(Canvas.GetLeft(t), Canvas.GetTop(t), w, h);
                }
                default: // Rectangle / Ellipse
                    return new Rect(Canvas.GetLeft(Element), Canvas.GetTop(Element),
                        Element.Width, Element.Height);
            }
        }
    }

    /// <summary>Proportionally remap the shape from one bounding box to another (resize).</summary>
    public void Remap(Rect from, Rect to)
    {
        if (from.Width <= 0 || from.Height <= 0) return;
        double MapX(double x) => to.X + (x - from.X) / from.Width * to.Width;
        double MapY(double y) => to.Y + (y - from.Y) / from.Height * to.Height;

        switch (Kind)
        {
            case VKind.Line:
            {
                var l = (Line)Element;
                double x1 = MapX(l.X1), y1 = MapY(l.Y1), x2 = MapX(l.X2), y2 = MapY(l.Y2);
                l.X1 = x1; l.Y1 = y1; l.X2 = x2; l.Y2 = y2;
                break;
            }
            case VKind.Path:
            {
                var p = (Polyline)Element;
                var pts = new PointCollection(p.Points.Count);
                foreach (var pt in p.Points) pts.Add(new Point(MapX(pt.X), MapY(pt.Y)));
                p.Points = pts;
                break;
            }
            case VKind.Text:
            {
                var t = (TextBlock)Element;
                Canvas.SetLeft(t, to.X);
                Canvas.SetTop(t, to.Y);
                if (from.Height > 0) t.FontSize = Math.Max(1, t.FontSize * to.Height / from.Height);
                break;
            }
            default:
                Canvas.SetLeft(Element, to.X);
                Canvas.SetTop(Element, to.Y);
                Element.Width = Math.Max(1, to.Width);
                Element.Height = Math.Max(1, to.Height);
                break;
        }
    }

    public void Move(double dx, double dy) => Remap(Bounds, Bounds with { X = Bounds.X + dx, Y = Bounds.Y + dy });

    // ---------------------------------------------------------------- SVG

    public string ToSvg()
    {
        return Kind switch
        {
            VKind.Rectangle => RectSvg(),
            VKind.Ellipse => EllipseSvg(),
            VKind.Line => LineSvg(),
            VKind.Path => PathSvg(),
            VKind.Text => TextSvg(),
            _ => ""
        };
    }

    private string RectSvg()
    {
        var r = (Rectangle)Element;
        var b = Bounds;
        var sb = new StringBuilder();
        sb.Append($"<rect x=\"{F(b.X)}\" y=\"{F(b.Y)}\" width=\"{F(b.Width)}\" height=\"{F(b.Height)}\"");
        if (r.RadiusX > 0) sb.Append($" rx=\"{F(r.RadiusX)}\" ry=\"{F(r.RadiusY)}\"");
        sb.Append(Paint(r.Fill, r.Stroke, r.StrokeThickness));
        sb.Append("/>");
        return sb.ToString();
    }

    private string EllipseSvg()
    {
        var e = (Ellipse)Element;
        var b = Bounds;
        return $"<ellipse cx=\"{F(b.X + b.Width / 2)}\" cy=\"{F(b.Y + b.Height / 2)}\" " +
               $"rx=\"{F(b.Width / 2)}\" ry=\"{F(b.Height / 2)}\"{Paint(e.Fill, e.Stroke, e.StrokeThickness)}/>";
    }

    private string LineSvg()
    {
        var l = (Line)Element;
        return $"<line x1=\"{F(l.X1)}\" y1=\"{F(l.Y1)}\" x2=\"{F(l.X2)}\" y2=\"{F(l.Y2)}\"" +
               $"{Paint(null, l.Stroke, l.StrokeThickness)} stroke-linecap=\"round\"/>";
    }

    private string PathSvg()
    {
        var p = (Polyline)Element;
        var pts = string.Join(" ", p.Points.Select(pt => $"{F(pt.X)},{F(pt.Y)}"));
        return $"<polyline points=\"{pts}\"{Paint(p.Fill, p.Stroke, p.StrokeThickness)} " +
               "stroke-linejoin=\"round\" stroke-linecap=\"round\"/>";
    }

    private string TextSvg()
    {
        var t = (TextBlock)Element;
        double x = Canvas.GetLeft(t), y = Canvas.GetTop(t) + t.FontSize * 0.8;
        string esc = System.Security.SecurityElement.Escape(t.Text) ?? "";
        var (fill, op) = ColorParts(t.Foreground);
        return $"<text x=\"{F(x)}\" y=\"{F(y)}\" font-family=\"Segoe UI, sans-serif\" " +
               $"font-size=\"{F(t.FontSize)}\" fill=\"{fill}\"{op}>{esc}</text>";
    }

    private static string Paint(Brush? fill, Brush? stroke, double strokeWidth)
    {
        var sb = new StringBuilder();
        var (f, fo) = ColorParts(fill);
        sb.Append($" fill=\"{f}\"{fo}");
        var (s, so) = ColorParts(stroke);
        sb.Append($" stroke=\"{s}\"{so}");
        if (stroke is SolidColorBrush sc && sc.Color.A > 0)
            sb.Append($" stroke-width=\"{F(strokeWidth)}\"");
        return sb.ToString();
    }

    /// <summary>Returns (colour-or-none, optional opacity-attribute-suffix).</summary>
    private static (string colour, string opacity) ColorParts(Brush? brush)
    {
        if (brush is not SolidColorBrush s || s.Color.A == 0) return ("none", "");
        var c = s.Color;
        string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        string op = c.A < 255 ? $" opacity=\"{(c.A / 255.0).ToString("0.##", CultureInfo.InvariantCulture)}\"" : "";
        return (hex, op);
    }
}
