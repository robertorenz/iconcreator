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
using IconCreator.Localization;
using IconCreator.Model;
using IconCreator.Views;
using Microsoft.Win32;

namespace IconCreator;

public partial class MainWindow : Window
{
    /// <summary>One open document (an editor tab) with its own undo history.</summary>
    private sealed class DocSession
    {
        public required IconDocument Doc { get; init; }
        public IconSlice Active { get; set; } = null!;
        public readonly Stack<UndoCommand> Undo = new();
        public readonly Stack<UndoCommand> Redo = new();
    }

    private readonly List<DocSession> _sessions = new();
    private DocSession _cur = null!;

    // Accessors over the active session, so the rest of the code is unchanged.
    private IconDocument _doc => _cur?.Doc!;
    private IconSlice _active { get => _cur?.Active!; set => _cur!.Active = value; }
    private Stack<UndoCommand> _undo => _cur.Undo;
    private Stack<UndoCommand> _redo => _cur.Redo;

    private ToolKind _tool = ToolKind.Pencil;
    private Color _color = Colors.Black;
    private int _brushSize = 1;
    private int _tolerance = 0;
    private double _zoom = 8;

    private bool _drawing;
    private int _startX, _startY, _lastX, _lastY;
    private int[]? _strokeSnapshot;

    private readonly Dictionary<int, Border> _sizeRows = new();
    private readonly List<ToggleButton> _toolButtons = new();

    private static readonly (ToolKind kind, string glyph, string key, string hint)[] Tools =
    {
        (ToolKind.Pencil,          "✏",  "tool.pencil",       "B"),
        (ToolKind.Eraser,          "▭",  "tool.eraser",       "E"),
        (ToolKind.Fill,            "🪣",  "tool.fill",         "G"),
        (ToolKind.Eyedropper,      "💧",  "tool.eyedropper",   "I"),
        (ToolKind.Line,            "╱",  "tool.line",         "L"),
        (ToolKind.Rectangle,       "▢",  "tool.rect",         "R"),
        (ToolKind.RectangleFilled, "▣",  "tool.rectFilled",   "Shift+R"),
        (ToolKind.Ellipse,         "◯",  "tool.ellipse",      "O"),
        (ToolKind.EllipseFilled,   "●",  "tool.ellipseFilled","Shift+O"),
    };

    private readonly AppSettings _settings = AppSettings.Load();

    public MainWindow()
    {
        InitializeComponent();
        NativeChrome.ApplyDarkTitleBar(this);

        Loc.Language = _settings.Language;
        Loc.Changed += ApplyLanguage;

        BuildToolPalette();
        BuildPalette();

        AllowDrop = true;
        PreviewDragOver += OnFileDragOver;
        PreviewDrop += OnFileDrop;

        Loaded += (_, _) => { NewDocument(IconDocument.StandardSizes, select: 32); FitZoom(); ApplyLanguage(); };
    }

    // ============================ Localisation ============================

    private Popup? _langPopup;

    private void OnLanguageMenu(object sender, RoutedEventArgs e)
    {
        if (_langPopup != null) { _langPopup.IsOpen = false; _langPopup = null; }

        var panel = new StackPanel { MinWidth = 150 };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("language"),
            Style = (Style)Application.Current.Resources["Label.Section"],
            Margin = new Thickness(8, 6, 8, 6)
        });

        foreach (var lang in Loc.All)
        {
            bool current = lang == Loc.Language;
            var btn = new Button
            {
                Content = (current ? "✓  " : "     ") + Loc.DisplayName(lang),
                Style = (Style)Application.Current.Resources["Button.Flat"],
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = current ? (Brush)Application.Current.Resources["Brush.Accent"]
                                     : (Brush)Application.Current.Resources["Brush.Text"]
            };
            var captured = lang;
            btn.Click += (_, _) =>
            {
                _langPopup!.IsOpen = false;
                Loc.Language = captured;          // raises Loc.Changed -> ApplyLanguage
                _settings.Language = captured;
                _settings.Save();
            };
            panel.Children.Add(btn);
        }

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["Brush.Elevated"],
            BorderBrush = (Brush)Application.Current.Resources["Brush.BorderStrong"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16, ShadowDepth = 3, Opacity = 0.5, Direction = 270, Color = Colors.Black
            },
            Child = panel
        };

        _langPopup = new Popup
        {
            PlacementTarget = BtnLang,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = border,
            IsOpen = true
        };
    }

    /// <summary>Re-apply every visible string for the current language.</summary>
    private void ApplyLanguage()
    {
        // Toolbar
        BtnNew.Content = Loc.T("new");
        BtnOpen.Content = Loc.T("open");
        BtnRecent.Content = Loc.T("recent");
        BtnSave.Content = Loc.T("save");
        BtnSaveAs.Content = Loc.T("saveAs");
        BtnImport.Content = Loc.T("import");
        BtnExportPng.Content = Loc.T("exportPng");
        BtnUndo.Content = Loc.T("undo");
        BtnRedo.Content = Loc.T("redo");
        BtnClear.Content = Loc.T("clear");
        BtnExportIco.Content = Loc.T("exportIco");
        BtnVector.Content = "✒ " + Loc.T("vector");
        BtnVector.ToolTip = Loc.T("vectorTip");
        BtnLang.ToolTip = Loc.T("language");

        // Right panel
        LblColour.Text = Loc.T("colour");
        BtnChooseColour.Content = Loc.T("chooseColour");
        LblBrush.Text = Loc.T("brushSize");
        LblTolerance.Text = Loc.T("fillTolerance");
        AlphaBlendCheck.Content = Loc.T("alphaBlend");
        GridCheck.Content = Loc.T("showGrid");
        LblZoom.Text = Loc.T("zoom");
        BtnFitZoom.Content = Loc.T("fitWindow");
        LblResolutions.Text = Loc.T("resolutions");
        LblExportTick.Text = Loc.T("exportTick");

        // Import bar
        ImportPlacingLabel.Text = Loc.T("placingImage");
        BtnImpFit.Content = Loc.T("fit");
        BtnImpCenter.Content = Loc.T("center");
        BtnImpReset.Content = Loc.T("reset");
        ImportAllSizes.Content = Loc.T("allSizes");
        ImportAllSizes.ToolTip = Loc.T("allSizesTip");
        BtnImpCancel.Content = Loc.T("cancel");
        BtnImpApply.Content = Loc.T("apply");

        // Tool tooltips + current tool status
        foreach (var b in _toolButtons)
        {
            var kind = (ToolKind)b.Tag!;
            var t = Tools.First(x => x.kind == kind);
            b.ToolTip = $"{Loc.T(t.key)}  ({t.hint})";
        }
        if (_cur != null)
        {
            StatusTool.Text = Loc.T(Tools.First(x => x.kind == _tool).key);
            RefreshTabStrip();
            UpdateTitle();
        }
        if (StatusHint != null && !_importing) StatusHint.Text = Loc.T("ready");
    }

    // ============================ Documents & tabs ============================

    /// <summary>Create a new document in its own tab and switch to it.</summary>
    private void NewDocument(int[] sizes, int select = 32)
    {
        var doc = new IconDocument(sizes);
        var active = doc.Find(select) ?? doc.Slices[^1];
        var session = new DocSession { Doc = doc, Active = active };
        _sessions.Add(session);
        ActivateSession(session);
        doc.IsDirty = false;
        UpdateTitle();
        RefreshTabStrip();
        SetColor(_color);
    }

    private void ActivateSession(DocSession session)
    {
        _cur = session;
        BuildSizeList();
        SetActive(session.Active);
        UpdateTitle();
        RefreshTabStrip();
    }

    private void CloseSession(DocSession session)
    {
        if (session.Doc.IsDirty)
        {
            ActivateSession(session);   // show what is about to close
            if (!ConfirmDiscard()) return;
        }

        int idx = _sessions.IndexOf(session);
        _sessions.Remove(session);

        if (_sessions.Count == 0)
        {
            NewDocument(IconDocument.StandardSizes, 32);   // always keep one open
            FitZoom();
            return;
        }

        if (_cur == session)
            ActivateSession(_sessions[Math.Min(idx, _sessions.Count - 1)]);
        else
            RefreshTabStrip();
    }

    private void RefreshTabStrip()
    {
        if (TabStrip == null) return;
        TabStrip.Children.Clear();
        foreach (var s in _sessions)
            TabStrip.Children.Add(BuildTab(s));

        var add = new Button
        {
            Content = "+",
            Style = (Style)Application.Current.Resources["Button.Flat"],
            Foreground = (Brush)Application.Current.Resources["Brush.TextDim"],
            FontSize = 16,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(2, 0, 0, 4),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"{Loc.T("new")} (Ctrl+N)"
        };
        add.Click += (_, _) => { NewDocument(IconDocument.StandardSizes, 32); FitZoom(); };
        TabStrip.Children.Add(add);
    }

    private Border BuildTab(DocSession s)
    {
        bool active = s == _cur;
        string name = s.Doc.FilePath is null ? Loc.T("untitled") : Path.GetFileName(s.Doc.FilePath);

        var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        if (s.Doc.IsDirty)
            stack.Children.Add(new TextBlock
            {
                Text = "●", FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["Brush.Accent"],
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
            });

        stack.Children.Add(new TextBlock
        {
            Text = name, FontSize = 12.5,
            Foreground = active ? (Brush)Application.Current.Resources["Brush.Text"]
                                : (Brush)Application.Current.Resources["Brush.TextDim"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160
        });

        var close = new Button
        {
            Content = "✕", Width = 18, Height = 18, Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0), FontSize = 9,
            Style = (Style)Application.Current.Resources["Button.Flat"],
            Foreground = (Brush)Application.Current.Resources["Brush.TextFaint"],
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = $"{Loc.T("close")} (Ctrl+W)"
        };
        close.Click += (_, _) => CloseSession(s);
        stack.Children.Add(close);

        var tab = new Border
        {
            Padding = new Thickness(12, 7, 6, 7),
            Margin = new Thickness(0, 0, 4, 4),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Background = active ? (Brush)Application.Current.Resources["Brush.Window"]
                                : (Brush)Application.Current.Resources["Brush.PanelAlt"],
            BorderBrush = active ? (Brush)Application.Current.Resources["Brush.Border"] : Brushes.Transparent,
            BorderThickness = new Thickness(1, 1, 1, 0),
            Cursor = Cursors.Hand,
            Child = stack
        };
        tab.MouseLeftButtonUp += (_, _) => { if (s != _cur) ActivateSession(s); };
        tab.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) CloseSession(s); };
        return tab;
    }

    private void SetActive(IconSlice slice)
    {
        if (_importing) EndImport();   // placement is tied to one slice
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
        if (_importing) LayoutImportLayer();
    }

    // ============================ Tool palette ============================

    private void BuildToolPalette()
    {
        foreach (var (kind, glyph, key, hint) in Tools)
        {
            var btn = new ToggleButton
            {
                Style = (Style)Resources["Button.Tool"] ?? (Style)Application.Current.Resources["Button.Tool"],
                Content = glyph,
                Margin = new Thickness(0, 0, 0, 6),
                ToolTip = $"{Loc.T(key)}  ({hint})",
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
        StatusTool.Text = Loc.T(Tools.First(t => t.kind == kind).key);
        StatusHint.Text = kind switch
        {
            ToolKind.Eyedropper => Loc.T("hint.eyedropper"),
            ToolKind.Fill => Loc.T("hint.fill"),
            ToolKind.Line or ToolKind.Rectangle or ToolKind.RectangleFilled
                or ToolKind.Ellipse or ToolKind.EllipseFilled => Loc.T("hint.shape"),
            _ => Loc.T("ready")
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
                ToolTip = c.A == 0 ? Loc.T("color.transparent") : $"#{c.R:X2}{c.G:X2}{c.B:X2}"
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
        if (_importing) return;   // drawing is disabled while placing an image
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
                Drawing.Stamp(_active.Buffer, px, py, Argb, _brushSize, BlendActive());
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
                Drawing.Line(_active.Buffer, _lastX, _lastY, px, py, Argb, _brushSize, BlendActive());
                break;
            case ToolKind.Eraser:
                Drawing.Line(_active.Buffer, _lastX, _lastY, px, py, 0, _brushSize, false);
                break;
            case ToolKind.Line:
                _active.Buffer.Restore(_strokeSnapshot!);
                Drawing.Line(_active.Buffer, _startX, _startY, px, py, Argb, _brushSize, BlendActive());
                break;
            case ToolKind.Rectangle:
                _active.Buffer.Restore(_strokeSnapshot!);
                Drawing.Rectangle(_active.Buffer, _startX, _startY, px, py, Argb, _brushSize, false, BlendActive());
                break;
            case ToolKind.RectangleFilled:
                _active.Buffer.Restore(_strokeSnapshot!);
                Drawing.Rectangle(_active.Buffer, _startX, _startY, px, py, Argb, _brushSize, true, BlendActive());
                break;
            case ToolKind.Ellipse:
                _active.Buffer.Restore(_strokeSnapshot!);
                Drawing.Ellipse(_active.Buffer, _startX, _startY, px, py, Argb, _brushSize, false, BlendActive());
                break;
            case ToolKind.EllipseFilled:
                _active.Buffer.Restore(_strokeSnapshot!);
                Drawing.Ellipse(_active.Buffer, _startX, _startY, px, py, Argb, _brushSize, true, BlendActive());
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

    /// <summary>
    /// Effective blend mode for the current foreground colour. A fully transparent
    /// colour must overwrite (write alpha 0) rather than blend — blending transparent
    /// over a pixel is a no-op, so it would appear to "do nothing".
    /// </summary>
    private bool BlendActive() => _alphaBlend() && _color.A != 0;

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

    /// <summary>Ctrl + mouse wheel zooms, anchored at the pixel under the cursor.</summary>
    private void OnCanvasWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || _active == null) return;
        e.Handled = true; // suppress the ScrollViewer's own scroll

        double oldZoom = _zoom;
        Point imgPos = e.GetPosition(EditImage);   // cursor within the image
        Point vpPos = e.GetPosition(CanvasScroll);  // cursor within the viewport

        double step = Math.Max(1, Math.Round(oldZoom * 0.2));
        double newZoom = Math.Clamp(oldZoom + (e.Delta > 0 ? step : -step),
                                    ZoomSlider.Minimum, ZoomSlider.Maximum);
        if (Math.Abs(newZoom - oldZoom) < 0.5) return;

        ZoomSlider.Value = newZoom;   // fires OnZoomChanged → RenderActive resizes the image
        CanvasScroll.UpdateLayout();

        // Keep the same image point under the cursor after the resize.
        var newLocal = new Point(imgPos.X * newZoom / oldZoom, imgPos.Y * newZoom / oldZoom);
        Point moved = EditImage.TranslatePoint(newLocal, CanvasScroll);
        CanvasScroll.ScrollToHorizontalOffset(CanvasScroll.HorizontalOffset + (moved.X - vpPos.X));
        CanvasScroll.ScrollToVerticalOffset(CanvasScroll.VerticalOffset + (moved.Y - vpPos.Y));
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
        var sizes = NewIconDialog.Show(this);
        if (sizes == null) return;
        NewDocument(sizes, sizes.Contains(32) ? 32 : sizes[0]);
        FitZoom();
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Icon (*.ico)|*.ico|" + ImageIO.OpenFilter };
        if (dlg.ShowDialog(this) != true) return;
        OpenPath(dlg.FileName);
    }

    private void OpenPath(string path)
    {
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(Loc.T("err.fileMissing"), path);

            var frames = ImageIO.LoadFrames(path);
            if (frames.Count == 0) throw new InvalidOperationException(Loc.T("err.noImages"));

            var sizes = frames.Select(f => f.PixelWidth)
                              .Where(w => w is >= 8 and <= 256)
                              .Distinct().OrderBy(w => w).ToArray();
            if (sizes.Length == 0) sizes = IconDocument.StandardSizes;

            NewDocument(sizes, sizes.Contains(32) ? 32 : sizes[0]);
            foreach (var slice in _doc.Slices)
                slice.Buffer.LoadFrom(ImageIO.BestFrameFor(frames, slice.Size));

            bool isIco = path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);
            _doc.FilePath = isIco ? path : null;
            _doc.IsDirty = false;
            UpdateTitle();
            FitZoom();
            if (isIco) RecentFiles.Add(path);
            StatusHint.Text = Loc.T("msg.opened", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            RecentFiles.Remove(path);
            ModalDialog.Error(this, Loc.T("err.openTitle"), ex.Message);
        }
    }

    private Popup? _recentPopup;

    private void OnRecent(object sender, RoutedEventArgs e)
    {
        if (_recentPopup != null) { _recentPopup.IsOpen = false; _recentPopup = null; }

        var list = RecentFiles.Load();
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("recentIcons"),
            Style = (Style)Application.Current.Resources["Label.Section"],
            Margin = new Thickness(8, 6, 8, 6)
        });

        if (list.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("noRecent"),
                Foreground = (Brush)Application.Current.Resources["Brush.TextFaint"],
                Margin = new Thickness(8, 4, 8, 10)
            });
        }
        else
        {
            foreach (var path in list)
                panel.Children.Add(BuildRecentRow(path));

            panel.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Height = 1,
                Fill = (Brush)Application.Current.Resources["Brush.Border"],
                Margin = new Thickness(6, 6, 6, 6)
            });

            var clear = new Button
            {
                Content = Loc.T("clearList"),
                Style = (Style)Application.Current.Resources["Button.Flat"],
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = (Brush)Application.Current.Resources["Brush.TextDim"]
            };
            clear.Click += (_, _) => { RecentFiles.Clear(); _recentPopup!.IsOpen = false; };
            panel.Children.Add(clear);
        }

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["Brush.Elevated"],
            BorderBrush = (Brush)Application.Current.Resources["Brush.BorderStrong"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            MinWidth = 320,
            MaxWidth = 460,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16, ShadowDepth = 3, Opacity = 0.5, Direction = 270,
                Color = Colors.Black
            },
            Child = panel
        };

        _recentPopup = new Popup
        {
            PlacementTarget = BtnRecent,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = border,
            IsOpen = true
        };
    }

    private Button BuildRecentRow(string path)
    {
        var content = new StackPanel { Margin = new Thickness(4, 3, 4, 3) };
        content.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(path),
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["Brush.Text"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        content.Children.Add(new TextBlock
        {
            Text = Path.GetDirectoryName(path) ?? "",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["Brush.TextFaint"],
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var btn = new Button
        {
            Content = content,
            Style = (Style)Application.Current.Resources["Button.Flat"],
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 2, 6, 2),
            ToolTip = path
        };
        var captured = path;
        btn.Click += (_, _) =>
        {
            _recentPopup!.IsOpen = false;
            OpenPath(captured);   // opens in a new tab
        };
        return btn;
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
            RecentFiles.Add(path);
            StatusHint.Text = Loc.T("msg.saved", Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            ModalDialog.Error(this, Loc.T("err.saveTitle"), ex.Message);
        }
    }

    private void OnExportIco(object sender, RoutedEventArgs e) => OnSaveAs(sender, e);

    private void OnVectorEditor(object sender, RoutedEventArgs e)
    {
        new VectorEditorWindow(this).Show();
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = ImageIO.OpenFilter };
        if (dlg.ShowDialog(this) != true) return;
        ImportFromFile(dlg.FileName);
    }

    private void ImportFromFile(string path)
    {
        try
        {
            var frames = ImageIO.LoadFrames(path);
            if (frames.Count == 0) throw new InvalidOperationException(Loc.T("err.noImageData"));

            // Use the highest-resolution frame available for the best scaling quality.
            var img = frames.OrderByDescending(f => f.PixelWidth).First();
            BeginImport(img, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            ModalDialog.Error(this, Loc.T("err.importTitle"), ex.Message);
        }
    }

    // ---- Drag & drop a file anywhere over the window to place it ----

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        bool open = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        if (ok)
            StatusHint.Text = open ? Loc.T("hint.dropOpen") : Loc.T("hint.dropPlace");
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        e.Handled = true;
        Activate();

        // Ctrl = open each file as its own document; otherwise place the first as a layer.
        if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0)
            foreach (var f in files) OpenPath(f);
        else
            ImportFromFile(files[0]);
    }

    // ============================ Import placement layer ============================

    private bool _importing;
    private BitmapSource? _importImage;
    private double _impX, _impY, _impW, _impH;     // in active-slice pixel space
    private Image? _impPreview;
    private System.Windows.Shapes.Rectangle? _impBox;

    private void BeginImport(BitmapSource img, string name)
    {
        if (_importing) EndImport();
        _importImage = img;
        _importing = true;

        FitPlacement();  // sensible default: aspect-fit, centred

        ImportLayer.Children.Clear();

        _impPreview = new Image { Source = img, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(_impPreview, BitmapScalingMode.HighQuality);
        ImportLayer.Children.Add(_impPreview);

        _impBox = new System.Windows.Shapes.Rectangle
        {
            Stroke = (Brush)Application.Current.Resources["Brush.Accent"],
            StrokeThickness = 1.4,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        ImportLayer.Children.Add(_impBox);

        // Move handle covers the whole rectangle.
        var move = new Thumb { Opacity = 0, Cursor = Cursors.SizeAll };
        move.DragDelta += (_, ev) =>
        {
            _impX += ev.HorizontalChange / _zoom;
            _impY += ev.VerticalChange / _zoom;
            LayoutImportLayer();
        };
        ImportLayer.Children.Add(move);
        _moveThumb = move;

        // Four corner resize handles.
        _corners = new Thumb[4];
        var tags = new[] { "TL", "TR", "BL", "BR" };
        var cursors = new[] { Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNESW, Cursors.SizeNWSE };
        for (int i = 0; i < 4; i++)
        {
            var t = new Thumb
            {
                Width = 12, Height = 12, Tag = tags[i], Cursor = cursors[i],
                Background = (Brush)Application.Current.Resources["Brush.Accent"]
            };
            t.Template = MakeHandleTemplate();
            t.DragDelta += OnCornerDrag;
            ImportLayer.Children.Add(t);
            _corners[i] = t;
        }

        ImportLayer.Visibility = Visibility.Visible;
        ImportBar.Visibility = Visibility.Visible;
        LayoutImportLayer();
        StatusHint.Text = Loc.T("hint.placing", name);
    }

    private Thumb? _moveThumb;
    private Thumb[]? _corners;

    private static ControlTemplate MakeHandleTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, Application.Current.Resources["Brush.Accent"]);
        factory.SetValue(Border.BorderBrushProperty, Brushes.White);
        factory.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        return new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
    }

    private void OnCornerDrag(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        double dx = e.HorizontalChange / _zoom, dy = e.VerticalChange / _zoom;
        string tag = (string)((Thumb)sender).Tag!;
        double x = _impX, y = _impY, w = _impW, h = _impH;

        double nw = tag is "TL" or "BL" ? w - dx : w + dx;
        double nh = tag is "TL" or "TR" ? h - dy : h + dy;
        nw = Math.Max(1, nw);
        nh = Math.Max(1, nh);

        // Hold Shift to keep the image's original aspect ratio.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _importImage != null)
        {
            double aspect = (double)_importImage.PixelWidth / _importImage.PixelHeight;
            if (Math.Abs(dy) > Math.Abs(dx)) nw = nh * aspect;   // vertical drag drives height
            else nh = nw / aspect;                               // horizontal drag drives width
        }

        // Keep the corner opposite the dragged one pinned in place.
        _impX = tag is "TL" or "BL" ? x + w - nw : x;
        _impY = tag is "TL" or "TR" ? y + h - nh : y;
        _impW = nw;
        _impH = nh;
        LayoutImportLayer();
    }

    private void LayoutImportLayer()
    {
        if (!_importing || _impPreview == null || _impBox == null || _corners == null || _moveThumb == null) return;

        double ox = _impX * _zoom, oy = _impY * _zoom, ow = _impW * _zoom, oh = _impH * _zoom;

        void Place(FrameworkElement el, double x, double y, double w, double h)
        {
            Canvas.SetLeft(el, x); Canvas.SetTop(el, y);
            el.Width = Math.Max(0, w); el.Height = Math.Max(0, h);
        }

        Place(_impPreview, ox, oy, ow, oh);
        Place(_impBox, ox, oy, ow, oh);
        Place(_moveThumb, ox, oy, ow, oh);

        var pts = new[] { (ox, oy), (ox + ow, oy), (ox, oy + oh), (ox + ow, oy + oh) };
        for (int i = 0; i < 4; i++)
        {
            Canvas.SetLeft(_corners[i], pts[i].Item1 - 6);
            Canvas.SetTop(_corners[i], pts[i].Item2 - 6);
        }
    }

    private void FitPlacement()
    {
        double a = _active.Size, iw = _importImage!.PixelWidth, ih = _importImage.PixelHeight;
        double scale = Math.Min(a / iw, a / ih);
        _impW = iw * scale; _impH = ih * scale;
        _impX = (a - _impW) / 2; _impY = (a - _impH) / 2;
    }

    private void OnImportFit(object sender, RoutedEventArgs e) { FitPlacement(); LayoutImportLayer(); }

    private void OnImportCenter(object sender, RoutedEventArgs e)
    {
        double a = _active.Size;
        _impX = (a - _impW) / 2; _impY = (a - _impH) / 2;
        LayoutImportLayer();
    }

    private void OnImportReset(object sender, RoutedEventArgs e)
    {
        double a = _active.Size;
        _impW = _importImage!.PixelWidth; _impH = _importImage.PixelHeight;
        _impX = (a - _impW) / 2; _impY = (a - _impH) / 2;
        LayoutImportLayer();
    }

    private void OnImportCancel(object sender, RoutedEventArgs e)
    {
        EndImport();
        StatusHint.Text = Loc.T("msg.importCancelled");
    }

    private void OnImportApply(object sender, RoutedEventArgs e)
    {
        if (_importImage == null) { EndImport(); return; }

        var targets = ImportAllSizes.IsChecked == true
            ? _doc.Slices.ToList()
            : new List<IconSlice> { _active };

        double a = _active.Size;
        foreach (var slice in targets)
        {
            double sc = slice.Size / a;
            var before = slice.Buffer.Snapshot();
            var placed = ImageIO.RasterizePlacement(_importImage,
                _impX * sc, _impY * sc, _impW * sc, _impH * sc, slice.Size);

            for (int i = 0; i < placed.Length; i++)
            {
                int argb = placed[i];
                if (((argb >> 24) & 0xFF) != 0)
                    slice.Buffer.Blend(i % slice.Size, i / slice.Size, argb);
            }
            slice.Buffer.Flush();
            _undo.Push(new UndoCommand(slice, before, slice.Buffer.Snapshot()));
        }
        _redo.Clear();

        EndImport();
        RenderActive();
        MarkDirty();
        StatusHint.Text = Loc.T(targets.Count > 1 ? "msg.appliedAll" : "msg.applied");
    }

    private void EndImport()
    {
        _importing = false;
        _importImage = null;
        _impPreview = null;
        _impBox = null;
        _moveThumb = null;
        _corners = null;
        ImportLayer.Children.Clear();
        ImportLayer.Visibility = Visibility.Collapsed;
        ImportBar.Visibility = Visibility.Collapsed;
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
            StatusHint.Text = Loc.T("msg.exported", Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            ModalDialog.Error(this, Loc.T("err.exportTitle"), ex.Message);
        }
    }

    // ============================ Housekeeping ============================

    private void MarkDirty()
    {
        _doc.IsDirty = true;
        UpdateTitle();
        RefreshTabStrip();
    }

    private void UpdateTitle() => Title = _doc.Title;

    private bool ConfirmDiscard()
    {
        if (!_doc.IsDirty) return true;
        return ModalDialog.Confirm(this, Loc.T("discardTitle"), Loc.T("discardMsg"),
            Loc.T("discard"), Loc.T("cancel"));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        int dirty = _sessions.Count(s => s.Doc.IsDirty);
        if (dirty > 0)
        {
            string msg = dirty == 1 ? Loc.T("closeOne") : Loc.T("closeMany", dirty);
            if (!ModalDialog.Confirm(this, Loc.T("discardTitle"), msg, Loc.T("close"), Loc.T("cancel")))
                e.Cancel = true;
        }
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
                case Key.W: if (_cur != null) CloseSession(_cur); e.Handled = true; return;
                case Key.Tab:
                    if (_sessions.Count > 1)
                    {
                        int i = _sessions.IndexOf(_cur);
                        int next = shift ? (i - 1 + _sessions.Count) % _sessions.Count
                                         : (i + 1) % _sessions.Count;
                        ActivateSession(_sessions[next]);
                    }
                    e.Handled = true;
                    return;
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
