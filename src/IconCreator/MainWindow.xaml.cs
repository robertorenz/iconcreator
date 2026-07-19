using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IconCreator.Editing;
using Drawing = IconCreator.Editing.Drawing;
using IconCreator.IO;
using IconCreator.Model;
using IconCreator.Views;
using Microsoft.Win32;

namespace IconCreator;

public partial class MainWindow : Window
{
    private IconDocument _doc = null!;
    private IconSlice _active = null!;

    private ToolKind _tool = ToolKind.Pencil;
    private Color _color = Colors.Black;
    private int _brushSize = 1;
    private int _tolerance = 0;
    private double _zoom = 8;

    private readonly Stack<UndoCommand> _undo = new();
    private readonly Stack<UndoCommand> _redo = new();

    private bool _drawing;
    private int _startX, _startY, _lastX, _lastY;
    private int[]? _strokeSnapshot;

    private readonly Dictionary<int, Border> _sizeRows = new();
    private readonly List<ToggleButton> _toolButtons = new();

    private static readonly (ToolKind kind, string glyph, string name, string hint)[] Tools =
    {
        (ToolKind.Pencil,          "✏",  "Pencil",           "B"),
        (ToolKind.Eraser,          "▭",  "Eraser",           "E"),
        (ToolKind.Fill,            "🪣",  "Flood Fill",       "G"),
        (ToolKind.Eyedropper,      "💧",  "Colour Picker",    "I"),
        (ToolKind.Line,            "╱",  "Line",             "L"),
        (ToolKind.Rectangle,       "▢",  "Rectangle",        "R"),
        (ToolKind.RectangleFilled, "▣",  "Filled Rectangle", "Shift+R"),
        (ToolKind.Ellipse,         "◯",  "Ellipse",          "O"),
        (ToolKind.EllipseFilled,   "●",  "Filled Ellipse",   "Shift+O"),
    };

    public MainWindow()
    {
        InitializeComponent();
        NativeChrome.ApplyDarkTitleBar(this);
        BuildToolPalette();
        BuildPalette();
        Loaded += (_, _) => { NewDocument(IconDocument.StandardSizes, select: 32); FitZoom(); };
    }

    // ============================ Document ============================

    private void NewDocument(int[] sizes, int select = 32)
    {
        _doc = new IconDocument(sizes);
        _undo.Clear();
        _redo.Clear();
        BuildSizeList();
        var target = _doc.Find(select) ?? _doc.Slices[^1];
        SetActive(target);
        _doc.IsDirty = false;
        UpdateTitle();
        SetColor(_color);
    }

    private void SetActive(IconSlice slice)
    {
        _active = slice;
        RenderActive();
        HighlightActiveRow();
        StatusSize.Text = slice.Label;
    }

    private void RenderActive()
    {
        EditImage.Source = _active.Buffer.Bitmap;
        double dim = _active.Size * _zoom;
        EditImage.Width = dim;
        EditImage.Height = dim;
        CheckerBorder.Background = BuildCheckerBrush(_zoom);
        UpdateGridOverlay();
        _active.Buffer.Flush();
    }

    // ============================ Tool palette ============================

    private void BuildToolPalette()
    {
        foreach (var (kind, glyph, name, hint) in Tools)
        {
            var btn = new ToggleButton
            {
                Style = (Style)Resources["Button.Tool"] ?? (Style)Application.Current.Resources["Button.Tool"],
                Content = glyph,
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = $"{name}  ({hint})",
                Tag = kind,
                IsChecked = kind == _tool
            };
            btn.Checked += (_, _) => SelectTool(kind);
            btn.Click += (_, _) => btn.IsChecked = true; // keep radio behaviour
            _toolButtons.Add(btn);
            ToolStack.Children.Add(btn);
        }
    }

    private void SelectTool(ToolKind kind)
    {
        _tool = kind;
        foreach (var b in _toolButtons)
            b.IsChecked = (ToolKind)b.Tag! == kind;
        StatusTool.Text = Tools.First(t => t.kind == kind).name;
        StatusHint.Text = kind switch
        {
            ToolKind.Eyedropper => "Click a pixel to sample its colour",
            ToolKind.Fill => "Click to flood-fill a region",
            ToolKind.Line or ToolKind.Rectangle or ToolKind.RectangleFilled
                or ToolKind.Ellipse or ToolKind.EllipseFilled => "Drag to draw the shape",
            _ => "Ready"
        };
    }

    // ============================ Colour ============================

    private void BuildPalette()
    {
        Color[] presets =
        {
            Colors.Black, Colors.White, Color.FromRgb(0x2F,0x81,0xD6), Color.FromRgb(0x1F,0x9E,0x8F),
            Color.FromRgb(0x2F,0xA3,0x6B), Color.FromRgb(0xD9,0xA3,0x35), Color.FromRgb(0xCF,0x5B,0x5B),
            Colors.Red, Colors.Orange, Colors.Gold, Colors.LimeGreen, Colors.DodgerBlue,
            Colors.SlateGray, Color.FromArgb(0,0,0,0)
        };
        foreach (var c in presets)
        {
            var b = new Border
            {
                Width = 20, Height = 20, Margin = new Thickness(0, 0, 5, 5),
                CornerRadius = new CornerRadius(3),
                BorderBrush = (Brush)Application.Current.Resources["Brush.Border"],
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Background = c.A == 0 ? BuildCheckerBrush(6) : new SolidColorBrush(c),
                ToolTip = c.A == 0 ? "Transparent" : $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            };
            var captured = c;
            b.MouseLeftButtonUp += (_, _) => SetColor(captured);
            PaletteStrip.Children.Add(b);
        }
    }

    private void SetColor(Color c)
    {
        _color = c;
        ColorSwatch.Background = c.A == 0 ? BuildCheckerBrush(8) : new SolidColorBrush(c);
        HexLabel.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private int Argb => (_color.A << 24) | (_color.R << 16) | (_color.G << 8) | _color.B;

    private void OnPickColor(object sender, RoutedEventArgs e)
    {
        var picked = ColorPickerDialog.Pick(this, _color);
        if (picked.HasValue) SetColor(picked.Value);
    }

    // ============================ Canvas interaction ============================

    private bool TryPixel(MouseEventArgs e, out int px, out int py)
    {
        var p = e.GetPosition(EditImage);
        px = (int)Math.Floor(p.X / _zoom);
        py = (int)Math.Floor(p.Y / _zoom);
        return _active.Buffer.InBounds(px, py);
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!TryPixel(e, out int px, out int py)) return;

        if (_tool == ToolKind.Eyedropper)
        {
            int argb = _active.Buffer.Get(px, py);
            SetColor(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            return;
        }

        _strokeSnapshot = _active.Buffer.Snapshot();
        _drawing = true;
        _startX = _lastX = px;
        _startY = _lastY = py;
        EditImage.CaptureMouse();

        switch (_tool)
        {
            case ToolKind.Pencil:
                Drawing.Stamp(_active.Buffer, px, py, Argb, _brushSize, _alphaBlend());
                break;
            case ToolKind.Eraser:
                Drawing.Stamp(_active.Buffer, px, py, 0, _brushSize, false);
                break;
            case ToolKind.Fill:
                Drawing.Fill(_active.Buffer, px, py, Argb, _tolerance);
                break;
        }
        _active.Buffer.Flush();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        bool inside = TryPixel(e, out int px, out int py);
        StatusCursor.Text = inside ? $"{px}, {py}" : "—";

        if (!_drawing) return;

        switch (_tool)
        {
            case ToolKind.Pencil:
                Drawing.Line(_active.Buffer, _lastX, _lastY, px, py, Argb, _brushSize, _alphaBlend());
                break;
            case ToolKind.Eraser:
                Drawing.Line(_active.Buffer, _lastX, _lastY, px, py, 0, _brushSize, false);
                break;
            case ToolKind.Line:
                _active.Buffer.Restore(_strokeSnapshot!);
                Drawing.Line(_active.Buffer, _startX, _startY, px, py, Argb, _brushSize, _alphaBlend());
                break;
            case ToolKind.Rectangle:
                _active.Buffer.Restore(_strokeSnapshot!);
                Drawing.Rectangle(_active.Buffer, _startX, _startY, px, py, Argb, _brushSize, false, _alphaBlend());
                break;
            case ToolKind.RectangleFilled:
                _active.Buffer.Restore(_strokeSnapshot!);
                Drawing.Rectangle(_active.Buffer, _startX, _startY, px, py, Argb, _brushSize, true, _alphaBlend());
                break;
            case ToolKind.Ellipse:
                _active.Buffer.Restore(_strokeSnapshot!);
                Drawing.Ellipse(_active.Buffer, _startX, _startY, px, py, Argb, _brushSize, false, _alphaBlend());
                break;
            case ToolKind.EllipseFilled:
                _active.Buffer.Restore(_strokeSnapshot!);
                Drawing.Ellipse(_active.Buffer, _startX, _startY, px, py, Argb, _brushSize, true, _alphaBlend());
                break;
            default:
                return;
        }
        _lastX = px; _lastY = py;
        _active.Buffer.Flush();
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;
        EditImage.ReleaseMouseCapture();
        CommitStroke();
    }

    private void OnCanvasMouseLeave(object sender, MouseEventArgs e)
    {
        StatusCursor.Text = "—";
    }

    private void CommitStroke()
    {
        if (_strokeSnapshot == null) return;
        var after = _active.Buffer.Snapshot();
        if (!after.AsSpan().SequenceEqual(_strokeSnapshot))
        {
            _undo.Push(new UndoCommand(_active, _strokeSnapshot, after));
            _redo.Clear();
            MarkDirty();
        }
        _strokeSnapshot = null;
    }

    private bool _alphaBlend() => AlphaBlendCheck.IsChecked == true;

    // ============================ Undo / redo ============================

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        var cmd = _undo.Pop();
        cmd.Slice.Buffer.Restore(cmd.Before);
        _redo.Push(cmd);
        if (cmd.Slice != _active) SetActive(cmd.Slice);
        RefreshThumbs();
        MarkDirty();
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0) return;
        var cmd = _redo.Pop();
        cmd.Slice.Buffer.Restore(cmd.After);
        _undo.Push(cmd);
        if (cmd.Slice != _active) SetActive(cmd.Slice);
        RefreshThumbs();
        MarkDirty();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        var before = _active.Buffer.Snapshot();
        _active.Buffer.Clear();
        _active.Buffer.Flush();
        _undo.Push(new UndoCommand(_active, before, _active.Buffer.Snapshot()));
        _redo.Clear();
        MarkDirty();
    }

    // ============================ Right-panel controls ============================

    private void OnBrushSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _brushSize = (int)e.NewValue;
        if (BrushValue != null) BrushValue.Text = $"{_brushSize} px";
    }

    private void OnToleranceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _tolerance = (int)e.NewValue;
        if (ToleranceValue != null) ToleranceValue.Text = _tolerance.ToString();
    }

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _zoom = Math.Round(e.NewValue);
        if (ZoomValue != null) ZoomValue.Text = $"{_zoom:0}×";
        if (_active != null) RenderActive();
    }

    private void OnFitZoom(object sender, RoutedEventArgs e) => FitZoom();

    private void FitZoom()
    {
        if (_active == null) return;
        double avail = Math.Min(CanvasScroll.ActualWidth, CanvasScroll.ActualHeight) - 60;
        if (avail < 40) avail = 256;
        double z = Math.Max(1, Math.Floor(avail / _active.Size));
        z = Math.Min(z, 32);
        ZoomSlider.Value = z;
    }

    private void OnGridToggle(object sender, RoutedEventArgs e) => UpdateGridOverlay();

    private void UpdateGridOverlay()
    {
        bool show = GridCheck.IsChecked == true && _zoom >= 5;
        GridOverlay.Fill = show ? BuildGridBrush(_zoom) : Brushes.Transparent;
    }

    // ============================ Sizes panel ============================

    private void BuildSizeList()
    {
        SizeList.Children.Clear();
        _sizeRows.Clear();

        foreach (var slice in _doc.Slices)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 0, 5),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Background = (Brush)Application.Current.Resources["Brush.PanelAlt"],
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var include = new CheckBox
            {
                IsChecked = slice.IncludeInExport,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 8, 0)
            };
            include.Checked += (_, _) => slice.IncludeInExport = true;
            include.Unchecked += (_, _) => slice.IncludeInExport = false;
            Grid.SetColumn(include, 0);

            int thumb = Math.Min(slice.Size, 40);
            var thumbBorder = new Border
            {
                Width = 44, Height = 44,
                CornerRadius = new CornerRadius(3),
                Background = BuildCheckerBrush(5),
                Child = new Image
                {
                    Source = slice.Buffer.Bitmap,
                    Width = thumb, Height = thumb,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            RenderOptions.SetBitmapScalingMode(thumbBorder.Child, BitmapScalingMode.NearestNeighbor);
            Grid.SetColumn(thumbBorder, 1);

            var label = new TextBlock
            {
                Text = slice.Label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                FontSize = 13
            };
            Grid.SetColumn(label, 2);

            grid.Children.Add(include);
            grid.Children.Add(thumbBorder);
            grid.Children.Add(label);
            row.Child = grid;

            var captured = slice;
            row.MouseLeftButtonUp += (_, _) => SetActive(captured);
            _sizeRows[slice.Size] = row;
            SizeList.Children.Add(row);
        }
    }

    private void HighlightActiveRow()
    {
        foreach (var (size, row) in _sizeRows)
        {
            bool on = _active != null && size == _active.Size;
            row.BorderBrush = on ? (Brush)Application.Current.Resources["Brush.Accent"] : Brushes.Transparent;
            row.Background = on
                ? (Brush)Application.Current.Resources["Brush.Elevated"]
                : (Brush)Application.Current.Resources["Brush.PanelAlt"];
        }
    }

    private void RefreshThumbs()
    {
        foreach (var slice in _doc.Slices) slice.Buffer.Flush();
    }

    // ============================ File commands ============================

    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var sizes = NewIconDialog.Show(this);
        if (sizes == null) return;
        NewDocument(sizes, sizes.Contains(32) ? 32 : sizes[0]);
        FitZoom();
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = "Icon (*.ico)|*.ico|" + ImageIO.OpenFilter };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var frames = ImageIO.LoadFrames(dlg.FileName);
            if (frames.Count == 0) throw new InvalidOperationException("No images found in file.");

            var sizes = frames.Select(f => f.PixelWidth)
                              .Where(w => w is >= 8 and <= 256)
                              .Distinct().OrderBy(w => w).ToArray();
            if (sizes.Length == 0) sizes = IconDocument.StandardSizes;

            NewDocument(sizes, sizes.Contains(32) ? 32 : sizes[0]);
            foreach (var slice in _doc.Slices)
                slice.Buffer.LoadFrom(ImageIO.BestFrameFor(frames, slice.Size));

            _doc.FilePath = dlg.FileName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? dlg.FileName : null;
            _doc.IsDirty = false;
            UpdateTitle();
            FitZoom();
            StatusHint.Text = $"Opened {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            ModalDialog.Error(this, "Could not open file", ex.Message);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_doc.FilePath == null) OnSaveAs(sender, e);
        else SaveTo(_doc.FilePath);
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Icon (*.ico)|*.ico",
            FileName = _doc.FilePath is null ? "icon.ico" : Path.GetFileName(_doc.FilePath)
        };
        if (dlg.ShowDialog(this) != true) return;
        SaveTo(dlg.FileName);
    }

    private void SaveTo(string path)
    {
        try
        {
            IcoEncoder.Save(path, _doc.Slices);
            _doc.FilePath = path;
            _doc.IsDirty = false;
            UpdateTitle();
            StatusHint.Text = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ModalDialog.Error(this, "Could not save icon", ex.Message);
        }
    }

    private void OnExportIco(object sender, RoutedEventArgs e) => OnSaveAs(sender, e);

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var frames = ImageIO.LoadFrames(dlg.FileName);
            if (frames.Count == 0) throw new InvalidOperationException("No image data found.");

            foreach (var slice in _doc.Slices)
            {
                var before = slice.Buffer.Snapshot();
                slice.Buffer.LoadFrom(ImageIO.BestFrameFor(frames, slice.Size));
                _undo.Push(new UndoCommand(slice, before, slice.Buffer.Snapshot()));
            }
            _redo.Clear();
            RenderActive();
            MarkDirty();
            StatusHint.Text = $"Imported {Path.GetFileName(dlg.FileName)} into all sizes";
        }
        catch (Exception ex)
        {
            ModalDialog.Error(this, "Could not import image", ex.Message);
        }
    }

    private void OnExportPng(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"icon_{_active.Size}.png"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            ImageIO.ExportPng(dlg.FileName, _active.Buffer);
            StatusHint.Text = $"Exported {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            ModalDialog.Error(this, "Could not export PNG", ex.Message);
        }
    }

    // ============================ Housekeeping ============================

    private void MarkDirty()
    {
        _doc.IsDirty = true;
        UpdateTitle();
    }

    private void UpdateTitle() => Title = _doc.Title;

    private bool ConfirmDiscard()
    {
        if (!_doc.IsDirty) return true;
        return ModalDialog.Confirm(this, "Discard changes?",
            "The current icon has unsaved changes. Continue and lose them?",
            "Discard", "Cancel");
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z: OnUndo(this, null!); e.Handled = true; return;
                case Key.Y: OnRedo(this, null!); e.Handled = true; return;
                case Key.S: OnSave(this, null!); e.Handled = true; return;
                case Key.N: OnNew(this, null!); e.Handled = true; return;
                case Key.O: OnOpen(this, null!); e.Handled = true; return;
            }
        }
        else
        {
            switch (e.Key)
            {
                case Key.B: SelectTool(ToolKind.Pencil); break;
                case Key.E: SelectTool(ToolKind.Eraser); break;
                case Key.G: SelectTool(ToolKind.Fill); break;
                case Key.I: SelectTool(ToolKind.Eyedropper); break;
                case Key.L: SelectTool(ToolKind.Line); break;
                case Key.R: SelectTool(shift ? ToolKind.RectangleFilled : ToolKind.Rectangle); break;
                case Key.O: SelectTool(shift ? ToolKind.EllipseFilled : ToolKind.Ellipse); break;
            }
        }
        base.OnPreviewKeyDown(e);
    }

    // ============================ Brushes ============================

    private static DrawingBrush BuildCheckerBrush(double cell)
    {
        var light = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x49));
        var dark = new SolidColorBrush(Color.FromRgb(0x2C, 0x31, 0x39));
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

    private static DrawingBrush BuildGridBrush(double cell)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), 1);
        var geo = new GeometryGroup();
        geo.Children.Add(new LineGeometry(new Point(cell, 0), new Point(cell, cell)));
        geo.Children.Add(new LineGeometry(new Point(0, cell), new Point(cell, cell)));
        var drawing = new GeometryDrawing(null, pen, geo);
        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell, cell),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }
}

/// <summary>A reversible edit to one slice: its pixels before and after a stroke.</summary>
public sealed record UndoCommand(IconSlice Slice, int[] Before, int[] After);
