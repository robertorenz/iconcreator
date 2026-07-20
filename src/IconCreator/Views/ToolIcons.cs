using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IconCreator.Views;

/// <summary>Crisp vector icons for the tool palettes (replaces text/emoji glyphs).</summary>
public static class ToolIcons
{
    private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF3)));

    // 24×24 filled silhouettes (M/L/C/S/relative commands only — no arcs).
    private const string Pencil    = "M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z";
    private const string Eraser    = "M15.14 3c-.51 0-1.02.2-1.41.59L2.59 14.73c-.78.77-.78 2.04 0 2.83L5.03 20h7.66l8.72-8.73c.79-.77.79-2.04 0-2.83l-4.85-4.85c-.39-.39-.9-.59-1.42-.59m-3.02 2 4.85 4.85-4.58 4.56-4.85-4.83L12.12 5z";
    private const string Bucket    = "M16.56 8.94 7.62 0 6.21 1.41l2.38 2.38-5.15 5.15c-.59.59-.59 1.54 0 2.13l5.5 5.5c.29.29.68.44 1.06.44s.77-.15 1.06-.44l5.5-5.5c.59-.58.59-1.53 0-2.12M5.21 10 10 5.21 14.79 10H5.21M19 11.5s-2 2.17-2 3.5c0 1.1.9 2 2 2s2-.9 2-2c0-1.33-2-3.5-2-3.5z";
    private const string Dropper   = "M20.71 5.63l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-3.12 3.12-1.93-1.91-1.41 1.41 1.42 1.42L3 17.25V21h3.75l8.96-8.96 1.42 1.42 1.41-1.41-1.92-1.92 3.12-3.12c.4-.4.4-1.03-.02-1.44M6.92 19 5 17.08l8.06-8.06 1.92 1.92L6.92 19z";
    private const string Cursor    = "M6 2 L6 19 L10.5 15 L13 21 L15.6 20 L13.1 14 L18.5 14 Z";

    public static FrameworkElement Make(string name) => name switch
    {
        "pencil"        => Wrap(Fill(Pencil)),
        "eraser"        => Wrap(Fill(Eraser)),
        "fill"          => Wrap(Fill(Bucket)),
        "eyedropper"    => Wrap(Fill(Dropper)),
        "select"        => Wrap(Fill(Cursor)),
        "line"          => Wrap(new Path { Data = Geometry.Parse("M4 20 L20 4"), Stroke = Ink, StrokeThickness = 2.5,
                                           StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }),
        "rect"          => Wrap(new Path { Data = new RectangleGeometry(new Rect(4, 7, 16, 10), 1.5, 1.5), Stroke = Ink, StrokeThickness = 2 }),
        "rectFilled"    => Wrap(new Path { Data = new RectangleGeometry(new Rect(4, 7, 16, 10), 1.5, 1.5), Fill = Ink }),
        "ellipse"       => Wrap(new Path { Data = new EllipseGeometry(new Point(12, 12), 8, 6), Stroke = Ink, StrokeThickness = 2 }),
        "ellipseFilled" => Wrap(new Path { Data = new EllipseGeometry(new Point(12, 12), 8, 6), Fill = Ink }),
        "path"          => Wrap(PathIcon()),
        "text"          => new TextBlock { Text = "T", FontWeight = FontWeights.Bold, FontSize = 17, Foreground = Ink,
                                           HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        _               => new TextBlock { Text = "?", Foreground = Ink }
    };

    private static Path Fill(string data) => new() { Data = Geometry.Parse(data), Fill = Ink };

    private static Viewbox Wrap(UIElement child) => new()
    {
        Width = 20, Height = 20, Stretch = Stretch.Uniform, Child = child
    };

    /// <summary>A poly-line with visible vertices — represents the multi-point Path tool.</summary>
    private static FrameworkElement PathIcon()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var pts = new[] { new Point(3, 18), new Point(9, 7), new Point(15, 15), new Point(21, 4) };
        canvas.Children.Add(new Polyline
        {
            Points = new PointCollection(pts),
            Stroke = Ink, StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        });
        foreach (var p in pts)
        {
            var dot = new Rectangle { Width = 4.5, Height = 4.5, Fill = Ink };
            Canvas.SetLeft(dot, p.X - 2.25);
            Canvas.SetTop(dot, p.Y - 2.25);
            canvas.Children.Add(dot);
        }
        return canvas;
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
