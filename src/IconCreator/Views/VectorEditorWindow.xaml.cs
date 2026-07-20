using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IconCreator.IO;
using IconCreator.Localization;
using IconCreator.Vector;
using Microsoft.Win32;

namespace IconCreator.Views;

public partial class VectorEditorWindow : Window
{
    private enum VTool { Select, Rectangle, Ellipse, Line, Path, Text }

    private const int Board = 256;

    private static readonly (VTool tool, string glyph, string key)[] Tools =
    {
        (VTool.Select,    "↖", "vec.select"),
        (VTool.Rectangle, "▭", "tool.rect"),
        (VTool.Ellipse,   "◯", "tool.ellipse"),
        (VTool.Line,      "╱", "tool.line"),
        (VTool.Path,      "✎", "vec.path"),
        (VTool.Text,      "T", "vec.text"),
    };

    private readonly List<VShape> _shapes = new();
    private readonly List<ToggleButton> _toolButtons = new();
    private VTool _tool = VTool.Select;

    private Color? _fill = Color.FromRgb(0x2F, 0x81, 0xD6);
    private Color? _stroke = null;
    private double _strokeWidth = 2;
    private double _fontSize = 28;

    private VShape? _selected;

    // interaction state
    private bool _drawing, _moving, _resizing;
    private Point _startPt, _lastPt;
    private FrameworkElement? _tempElement;
    private Rect _origBounds;
    private string _resizeCorner = "";
    private Polyline? _activePath;
    private TextBox? _activeText;

    public VectorEditorWindow(Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        NativeChrome.ApplyDarkTitleBar(this);
        BoardFrame.Background = BuildChecker(8);

        BuildTools();
        Loc.Changed += ApplyLanguage;
        Closed += (_, _) => Loc.Changed -= ApplyLanguage;

        UpdateSwatches();
        SelectTool(VTool.Select);
        ApplyLanguage();
    }

    // ============================ Tools ============================

    private void BuildTools()
    {
        foreach (var (tool, glyph, key) in Tools)
        {
            var btn = new ToggleButton
            {
                Style = (Style)Application.Current.Resources["Button.Tool"],
                Content = glyph,
                Margin = new Thickness(0, 0, 0, 6),
                Tag = tool,
                IsChecked = tool == _tool
            };
            btn.Checked += (_, _) => SelectTool(tool);
            btn.Click += (_, _) => btn.IsChecked = true;
            _toolButtons.Add(btn);
            ToolStack.Children.Add(btn);
        }
    }

    private void SelectTool(VTool tool)
    {
        CommitActiveText();
        FinishPath();
        _tool = tool;
        foreach (var b in _toolButtons) b.IsChecked = (VTool)b.Tag! == tool;
        StatusTool.Text = Loc.T(Tools.First(t => t.tool == tool).key);
        StatusHint.Text = tool switch
        {
            VTool.Select => Loc.T("vec.hint.select"),
            VTool.Path => Loc.T("vec.hint.path"),
            VTool.Text => Loc.T("vec.hint.text"),
            _ => Loc.T("vec.hint.draw"),
        };
        if (tool != VTool.Select) Select(null);
        ArtBoard.Cursor = tool == VTool.Select ? Cursors.Arrow : Cursors.Cross;
    }

    // ============================ Colour / style ============================

    private Brush FillBrush() => _fill is { } c ? new SolidColorBrush(c) : Brushes.Transparent;
    private Brush StrokeBrush(Color? fallback = null)
    {
        var col = _stroke ?? fallback;
        return col is { } c ? new SolidColorBrush(c) : Brushes.Transparent;
    }

    private void UpdateSwatches()
    {
        FillSwatch.Background = _fill is { } f ? new SolidColorBrush(f) : BuildChecker(5);
        StrokeSwatch.Background = _stroke is { } s ? new SolidColorBrush(s) : BuildChecker(5);
        FillNone.IsChecked = _fill is null;
        StrokeNone.IsChecked = _stroke is null;
    }

    private void OnPickFill(object sender, MouseButtonEventArgs e)
    {
        var picked = ColorPickerDialog.Pick(this, _fill ?? Colors.Black);
        if (picked is { } c) { _fill = c; UpdateSwatches(); ApplyStyleToSelection(); }
    }

    private void OnPickStroke(object sender, MouseButtonEventArgs e)
    {
        var picked = ColorPickerDialog.Pick(this, _stroke ?? Colors.Black);
        if (picked is { } c) { _stroke = c; UpdateSwatches(); ApplyStyleToSelection(); }
    }

    private void OnFillNone(object sender, RoutedEventArgs e)
    {
        if (FillNone.IsChecked == true) _fill = null;
        else if (_fill is null) _fill = Color.FromRgb(0x2F, 0x81, 0xD6);
        UpdateSwatches(); ApplyStyleToSelection();
    }

    private void OnStrokeNone(object sender, RoutedEventArgs e)
    {
        if (StrokeNone.IsChecked == true) _stroke = null;
        else if (_stroke is null) _stroke = Colors.Black;
        UpdateSwatches(); ApplyStyleToSelection();
    }

    private void OnStrokeWidth(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _strokeWidth = e.NewValue;
        if (StrokeWidthValue != null) StrokeWidthValue.Text = $"{_strokeWidth:0}";
        ApplyStyleToSelection();
    }

    private void OnFontSize(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _fontSize = e.NewValue;
        if (FontSizeValue != null) FontSizeValue.Text = $"{_fontSize:0}";
        if (_selected is { Kind: VKind.Text, Element: TextBlock tb }) tb.FontSize = _fontSize;
    }

    private void ApplyStyleToSelection()
    {
        if (_selected is null) return;
        switch (_selected.Element)
        {
            case Shape sh when _selected.Kind != VKind.Line:
                sh.Fill = FillBrush();
                sh.Stroke = StrokeBrush();
                sh.StrokeThickness = _strokeWidth;
                break;
            case Line ln:
                ln.Stroke = StrokeBrush(_fill);
                ln.StrokeThickness = Math.Max(0.5, _strokeWidth);
                break;
            case TextBlock tb:
                tb.Foreground = _fill is { } c ? new SolidColorBrush(c) : Brushes.Black;
                break;
        }
    }

    // ============================ Board interaction ============================

    private void OnBoardDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(ArtBoard);
        _startPt = _lastPt = pos;

        switch (_tool)
        {
            case VTool.Select:
                var hit = _shapes.FirstOrDefault(s => ReferenceEquals(s.Element, e.OriginalSource));
                Select(hit);
                if (hit != null)
                {
                    _moving = true;
                    _origBounds = hit.Bounds;
                    ArtBoard.CaptureMouse();
                }
                break;

            case VTool.Text:
                BeginText(pos);
                break;

            case VTool.Path:
                HandlePathClick(pos, e.ClickCount);
                break;

            default: // Rectangle / Ellipse / Line drag
                _drawing = true;
                _tempElement = CreateShapeElement(_tool, pos);
                ArtBoard.Children.Add(_tempElement);
                ArtBoard.CaptureMouse();
                break;
        }
    }

    private void OnBoardMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(ArtBoard);

        if (_drawing && _tempElement != null)
        {
            UpdateDragShape(_tool, _tempElement, _startPt, pos);
        }
        else if (_moving && _selected != null)
        {
            _selected.Remap(_selected.Bounds,
                _origBounds with { X = _origBounds.X + (pos.X - _startPt.X), Y = _origBounds.Y + (pos.Y - _startPt.Y) });
            ShowHandles();
        }
        _lastPt = pos;
    }

    private void OnBoardUp(object sender, MouseButtonEventArgs e)
    {
        ArtBoard.ReleaseMouseCapture();

        if (_drawing && _tempElement != null)
        {
            _drawing = false;
            var kind = _tool switch
            {
                VTool.Rectangle => VKind.Rectangle,
                VTool.Ellipse => VKind.Ellipse,
                _ => VKind.Line
            };
            var shape = new VShape(kind, _tempElement);
            // discard zero-size accidental clicks
            if (shape.Bounds.Width < 1 && shape.Bounds.Height < 1)
                ArtBoard.Children.Remove(_tempElement);
            else
            {
                _shapes.Add(shape);
                Select(shape);
            }
            _tempElement = null;
            UpdateCount();
        }
        _moving = false;
    }

    // ---- shape creation ----

    private FrameworkElement CreateShapeElement(VTool tool, Point at)
    {
        switch (tool)
        {
            case VTool.Line:
                return new Line { X1 = at.X, Y1 = at.Y, X2 = at.X, Y2 = at.Y,
                    Stroke = StrokeBrush(_fill), StrokeThickness = Math.Max(0.5, _strokeWidth),
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            case VTool.Ellipse:
            {
                var el = new Ellipse { Fill = FillBrush(), Stroke = StrokeBrush(), StrokeThickness = _strokeWidth };
                Canvas.SetLeft(el, at.X); Canvas.SetTop(el, at.Y);
                return el;
            }
            default:
            {
                var r = new Rectangle { Fill = FillBrush(), Stroke = StrokeBrush(), StrokeThickness = _strokeWidth };
                Canvas.SetLeft(r, at.X); Canvas.SetTop(r, at.Y);
                return r;
            }
        }
    }

    private static void UpdateDragShape(VTool tool, FrameworkElement el, Point start, Point cur)
    {
        if (tool == VTool.Line && el is Line l)
        {
            l.X2 = cur.X; l.Y2 = cur.Y;
            return;
        }
        double x = Math.Min(start.X, cur.X), y = Math.Min(start.Y, cur.Y);
        double w = Math.Abs(cur.X - start.X), h = Math.Abs(cur.Y - start.Y);
        Canvas.SetLeft(el, x); Canvas.SetTop(el, y);
        el.Width = w; el.Height = h;
    }

    // ---- path (polyline) ----

    private void HandlePathClick(Point pos, int clickCount)
    {
        if (clickCount == 2) { FinishPath(); return; }

        if (_activePath == null)
        {
            _activePath = new Polyline
            {
                Stroke = StrokeBrush(_fill) is SolidColorBrush sb && sb.Color.A > 0 ? StrokeBrush(_fill) : new SolidColorBrush(_fill ?? Colors.Black),
                StrokeThickness = Math.Max(0.5, _strokeWidth),
                Fill = _fill is { } && _stroke is null ? Brushes.Transparent : FillBrush(),
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            ArtBoard.Children.Add(_activePath);
        }
        _activePath.Points.Add(pos);
    }

    private void FinishPath()
    {
        if (_activePath == null) return;
        if (_activePath.Points.Count >= 2)
        {
            var shape = new VShape(VKind.Path, _activePath);
            _shapes.Add(shape);
            _activePath = null;
            UpdateCount();
            Select(shape);
        }
        else
        {
            ArtBoard.Children.Remove(_activePath);
            _activePath = null;
        }
    }

    // ---- text ----

    private void BeginText(Point at)
    {
        CommitActiveText();
        _activeText = new TextBox
        {
            MinWidth = 40,
            FontSize = _fontSize,
            Background = Brushes.Transparent,
            Foreground = _fill is { } c ? new SolidColorBrush(c) : Brushes.Black,
            BorderBrush = (Brush)Application.Current.Resources["Brush.Accent"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0)
        };
        Canvas.SetLeft(_activeText, at.X);
        Canvas.SetTop(_activeText, at.Y);
        ArtBoard.Children.Add(_activeText);
        _activeText.Focus();
        _activeText.LostKeyboardFocus += (_, _) => CommitActiveText();
        _activeText.KeyDown += (_, ev) =>
        {
            if (ev.Key == Key.Enter) { CommitActiveText(); ev.Handled = true; }
            else if (ev.Key == Key.Escape) { CancelActiveText(); ev.Handled = true; }
        };
    }

    private void CommitActiveText()
    {
        if (_activeText == null) return;
        var tbx = _activeText;
        _activeText = null;
        string text = tbx.Text.Trim();
        double x = Canvas.GetLeft(tbx), y = Canvas.GetTop(tbx);
        double fs = tbx.FontSize;
        var fg = tbx.Foreground;
        ArtBoard.Children.Remove(tbx);

        if (text.Length == 0) return;

        var tb = new TextBlock { Text = text, FontSize = fs, Foreground = fg, FontFamily = new FontFamily("Segoe UI") };
        Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y);
        ArtBoard.Children.Add(tb);
        tb.UpdateLayout();
        var shape = new VShape(VKind.Text, tb);
        _shapes.Add(shape);
        UpdateCount();
        Select(shape);
    }

    private void CancelActiveText()
    {
        if (_activeText == null) return;
        ArtBoard.Children.Remove(_activeText);
        _activeText = null;
    }

    // ============================ Selection & handles ============================

    private void Select(VShape? shape)
    {
        _selected = shape;
        ShowHandles();
        if (shape != null) SyncStyleFromShape(shape);
    }

    private void SyncStyleFromShape(VShape shape)
    {
        // Reflect the selected shape's style into the side panel.
        switch (shape.Element)
        {
            case Line ln:
                _stroke = (ln.Stroke as SolidColorBrush)?.Color;
                _strokeWidth = ln.StrokeThickness;
                break;
            case Shape sh:
                _fill = (sh.Fill as SolidColorBrush)?.Color is { } fc && fc.A > 0 ? fc : null;
                _stroke = (sh.Stroke as SolidColorBrush)?.Color is { } scc && scc.A > 0 ? scc : null;
                _strokeWidth = sh.StrokeThickness;
                break;
            case TextBlock tb:
                _fill = (tb.Foreground as SolidColorBrush)?.Color;
                _fontSize = tb.FontSize;
                FontSizeSlider.Value = Math.Clamp(_fontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
                break;
        }
        StrokeWidthSlider.Value = Math.Clamp(_strokeWidth, StrokeWidthSlider.Minimum, StrokeWidthSlider.Maximum);
        UpdateSwatches();
    }

    private void ShowHandles()
    {
        SelectionLayer.Children.Clear();
        if (_selected == null) return;

        var b = _selected.Bounds;
        var outline = new Rectangle
        {
            Width = Math.Max(1, b.Width), Height = Math.Max(1, b.Height),
            Stroke = (Brush)Application.Current.Resources["Brush.Accent"],
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(outline, b.X); Canvas.SetTop(outline, b.Y);
        SelectionLayer.Children.Add(outline);

        var corners = new[] { ("TL", b.Left, b.Top), ("TR", b.Right, b.Top), ("BL", b.Left, b.Bottom), ("BR", b.Right, b.Bottom) };
        foreach (var (tag, cx, cy) in corners)
        {
            var h = new Rectangle
            {
                Width = 8, Height = 8, Tag = tag,
                Fill = (Brush)Application.Current.Resources["Brush.Accent"],
                Stroke = Brushes.White, StrokeThickness = 1,
                Cursor = tag is "TL" or "BR" ? Cursors.SizeNWSE : Cursors.SizeNESW
            };
            Canvas.SetLeft(h, cx - 4); Canvas.SetTop(h, cy - 4);
            h.MouseLeftButtonDown += OnHandleDown;
            h.MouseMove += OnHandleMove;
            h.MouseLeftButtonUp += OnHandleUp;
            SelectionLayer.Children.Add(h);
        }
    }

    private void OnHandleDown(object sender, MouseButtonEventArgs e)
    {
        if (_selected == null) return;
        _resizing = true;
        _resizeCorner = (string)((Rectangle)sender).Tag!;
        _origBounds = _selected.Bounds;
        ((Rectangle)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnHandleMove(object sender, MouseEventArgs e)
    {
        if (!_resizing || _selected == null) return;
        var p = e.GetPosition(ArtBoard);
        double l = _origBounds.Left, t = _origBounds.Top, r = _origBounds.Right, bt = _origBounds.Bottom;
        switch (_resizeCorner)
        {
            case "TL": l = p.X; t = p.Y; break;
            case "TR": r = p.X; t = p.Y; break;
            case "BL": l = p.X; bt = p.Y; break;
            case "BR": r = p.X; bt = p.Y; break;
        }
        var nb = new Rect(Math.Min(l, r), Math.Min(t, bt), Math.Max(1, Math.Abs(r - l)), Math.Max(1, Math.Abs(bt - t)));
        _selected.Remap(_selected.Bounds, nb);
        ShowHandles();
        e.Handled = true;
    }

    private void OnHandleUp(object sender, MouseButtonEventArgs e)
    {
        _resizing = false;
        ((Rectangle)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    // ============================ Commands ============================

    private void OnDeleteSelected(object sender, RoutedEventArgs e) => DeleteSelected();

    private void DeleteSelected()
    {
        if (_selected == null) return;
        ArtBoard.Children.Remove(_selected.Element);
        _shapes.Remove(_selected);
        Select(null);
        UpdateCount();
    }

    private void OnClearAll(object sender, RoutedEventArgs e)
    {
        if (_shapes.Count == 0) return;
        if (!ModalDialog.Confirm(this, Loc.T("vec.clearAll"), Loc.T("discardMsg"), Loc.T("discard"), Loc.T("cancel")))
            return;
        foreach (var s in _shapes) ArtBoard.Children.Remove(s.Element);
        _shapes.Clear();
        Select(null);
        UpdateCount();
    }

    private void OnSaveSvg(object sender, RoutedEventArgs e)
    {
        CommitActiveText();
        FinishPath();
        Select(null);
        var dlg = new SaveFileDialog { Filter = "SVG image (*.svg)|*.svg", FileName = "drawing.svg" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            SvgWriter.Save(dlg.FileName, Board, _shapes);
            StatusHint.Text = Loc.T("vec.savedSvg", System.IO.Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            ModalDialog.Error(this, Loc.T("err.saveSvgTitle"), ex.Message);
        }
    }

    private void OnExportPng(object sender, RoutedEventArgs e)
    {
        CommitActiveText();
        FinishPath();
        Select(null);
        var dlg = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = "drawing.png" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            ArtBoard.UpdateLayout();
            var rtb = new RenderTargetBitmap(Board, Board, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(ArtBoard);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(dlg.FileName);
            enc.Save(fs);
            StatusHint.Text = Loc.T("msg.exported", System.IO.Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            ModalDialog.Error(this, Loc.T("err.exportTitle"), ex.Message);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if ((e.Key == Key.Delete || e.Key == Key.Back) && _activeText == null && _selected != null)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _tool == VTool.Path)
        {
            FinishPath();
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private void UpdateCount() => ShapeCount.Text = _shapes.Count == 1 ? "1 shape" : $"{_shapes.Count} shapes";

    // ============================ Localisation ============================

    private void ApplyLanguage()
    {
        Title = Loc.T("vec.title");
        TitleLabel.Text = Loc.T("vector");
        BtnSaveSvg.Content = Loc.T("vec.saveSvg");
        BtnExportPng.Content = Loc.T("vec.exportPng");
        LblFill.Text = Loc.T("vec.fill");
        LblStroke.Text = Loc.T("vec.stroke");
        LblStrokeWidth.Text = Loc.T("vec.strokeWidth");
        LblFontSize.Text = Loc.T("vec.fontSize");
        FillNone.Content = Loc.T("vec.none");
        StrokeNone.Content = Loc.T("vec.none");
        BtnDelete.Content = Loc.T("vec.delete");
        BtnClearAll.Content = Loc.T("vec.clearAll");
        foreach (var b in _toolButtons)
            b.ToolTip = Loc.T(Tools.First(t => t.tool == (VTool)b.Tag!).key);
        StatusTool.Text = Loc.T(Tools.First(t => t.tool == _tool).key);
    }

    // ============================ Helpers ============================

    private static DrawingBrush BuildChecker(double cell)
    {
        var light = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5));
        var dark = new SolidColorBrush(Color.FromRgb(0xD9, 0xDD, 0xE2));
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, cell * 2, cell * 2))));
        dg.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, cell, cell))));
        dg.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(cell, cell, cell, cell))));
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell * 2, cell * 2),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }
}
