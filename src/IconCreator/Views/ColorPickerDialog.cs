using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IconCreator.Localization;

namespace IconCreator.Views;

/// <summary>Themed RGBA colour picker with hex entry and a live preview.</summary>
public sealed class ColorPickerDialog : Window
{
    public Color SelectedColor { get; private set; }
    private bool _ok;
    private bool _syncing;

    private readonly Slider _r, _g, _b, _a;
    private readonly TextBox _hex;
    private readonly Border _preview;

    public ColorPickerDialog(Window? owner, Color initial)
    {
        Owner = owner;
        Title = Loc.T("color.title");
        Width = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["Brush.Panel"];
        NativeChrome.ApplyDarkTitleBar(this);
        SelectedColor = initial;

        var panel = new StackPanel { Margin = new Thickness(20) };

        _preview = new Border
        {
            Height = 60,
            CornerRadius = new CornerRadius(6),
            BorderBrush = (Brush)Application.Current.Resources["Brush.Border"],
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 16),
            Background = MakeCheckerBrush()
        };
        var previewFill = new Border { CornerRadius = new CornerRadius(6) };
        _preview.Child = previewFill;
        panel.Children.Add(_preview);

        _r = AddSlider(panel, "R", initial.R);
        _g = AddSlider(panel, "G", initial.G);
        _b = AddSlider(panel, "B", initial.B);
        _a = AddSlider(panel, "A", initial.A);

        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        hexRow.Children.Add(new TextBlock
        {
            Text = Loc.T("color.hex"),
            Width = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["Brush.TextDim"]
        });
        _hex = new TextBox { Width = 130, VerticalContentAlignment = VerticalAlignment.Center };
        _hex.LostFocus += (_, _) => ApplyHex();
        _hex.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) ApplyHex(); };
        hexRow.Children.Add(_hex);
        panel.Children.Add(hexRow);

        // Preset swatches
        var swatches = new WrapPanel { Margin = new Thickness(0, 16, 0, 4) };
        foreach (var c in PresetColors())
        {
            var sw = new Border
            {
                Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(c),
                BorderBrush = (Brush)Application.Current.Resources["Brush.Border"],
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var captured = c;
            sw.MouseLeftButtonUp += (_, _) => SetFromColor(Color.FromArgb((byte)_a.Value, captured.R, captured.G, captured.B));
            swatches.Children.Add(sw);
        }
        panel.Children.Add(swatches);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button { Content = Loc.T("cancel"), MinWidth = 84, Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
        cancel.Click += (_, _) => Close();
        var ok = new Button { Content = Loc.T("select"), MinWidth = 84, IsDefault = true, Style = (Style)Application.Current.Resources["Button.Accent"] };
        ok.Click += (_, _) => { _ok = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);

        Content = panel;
        UpdateFromSliders();
    }

    private Slider AddSlider(Panel parent, string label, byte value)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["Brush.TextDim"] };
        Grid.SetColumn(lbl, 0);

        var slider = new Slider { Minimum = 0, Maximum = 255, Value = value,
            VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 1 };
        Grid.SetColumn(slider, 1);

        var val = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right,
            Foreground = (Brush)Application.Current.Resources["Brush.Text"] };
        Grid.SetColumn(val, 2);
        val.Text = value.ToString();

        slider.ValueChanged += (_, _) => { val.Text = ((int)slider.Value).ToString(); if (!_syncing) UpdateFromSliders(); };

        row.Children.Add(lbl); row.Children.Add(slider); row.Children.Add(val);
        parent.Children.Add(row);
        return slider;
    }

    private void UpdateFromSliders()
    {
        SelectedColor = Color.FromArgb((byte)_a.Value, (byte)_r.Value, (byte)_g.Value, (byte)_b.Value);
        ((Border)_preview.Child).Background = new SolidColorBrush(SelectedColor);
        if (!_syncing)
        {
            _syncing = true;
            _hex.Text = $"#{SelectedColor.A:X2}{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
            _syncing = false;
        }
    }

    private void SetFromColor(Color c)
    {
        _syncing = true;
        _r.Value = c.R; _g.Value = c.G; _b.Value = c.B; _a.Value = c.A;
        _syncing = false;
        UpdateFromSliders();
    }

    private void ApplyHex()
    {
        var t = _hex.Text.Trim().TrimStart('#');
        try
        {
            byte a = 255, r, g, b;
            if (t.Length == 8)
            {
                a = byte.Parse(t.Substring(0, 2), NumberStyles.HexNumber);
                r = byte.Parse(t.Substring(2, 2), NumberStyles.HexNumber);
                g = byte.Parse(t.Substring(4, 2), NumberStyles.HexNumber);
                b = byte.Parse(t.Substring(6, 2), NumberStyles.HexNumber);
            }
            else if (t.Length == 6)
            {
                r = byte.Parse(t.Substring(0, 2), NumberStyles.HexNumber);
                g = byte.Parse(t.Substring(2, 2), NumberStyles.HexNumber);
                b = byte.Parse(t.Substring(4, 2), NumberStyles.HexNumber);
            }
            else return;
            SetFromColor(Color.FromArgb(a, r, g, b));
        }
        catch { /* ignore malformed input */ }
    }

    private static Color[] PresetColors() => new[]
    {
        Colors.Black, Colors.White, Color.FromRgb(0x2F,0x81,0xD6), Color.FromRgb(0x1F,0x9E,0x8F),
        Color.FromRgb(0x2F,0xA3,0x6B), Color.FromRgb(0xD9,0xA3,0x35), Color.FromRgb(0xCF,0x5B,0x5B),
        Color.FromRgb(0xE9,0xED,0xF2), Color.FromRgb(0x6C,0x75,0x81), Color.FromRgb(0x23,0x28,0x2F),
        Colors.Red, Colors.OrangeRed, Colors.Gold, Colors.LimeGreen, Colors.DodgerBlue, Colors.SlateGray
    };

    private static DrawingBrush MakeCheckerBrush()
    {
        var g = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            null, new RectangleGeometry(new Rect(0, 0, 16, 16)));
        var dg = new DrawingGroup();
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), null,
            new RectangleGeometry(new Rect(0, 0, 16, 16))));
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), null,
            new RectangleGeometry(new Rect(0, 0, 8, 8))));
        dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), null,
            new RectangleGeometry(new Rect(8, 8, 8, 8))));
        return new DrawingBrush(dg)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    public static Color? Pick(Window? owner, Color initial)
    {
        var dlg = new ColorPickerDialog(owner, initial);
        dlg.ShowDialog();
        return dlg._ok ? dlg.SelectedColor : null;
    }
}
