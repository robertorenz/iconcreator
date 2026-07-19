using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IconCreator.Model;

namespace IconCreator.Views;

/// <summary>Lets the user choose which resolutions a new icon should contain.</summary>
public sealed class NewIconDialog : Window
{
    private readonly Dictionary<int, CheckBox> _boxes = new();
    private bool _ok;

    public int[] SelectedSizes { get; private set; } = IconDocument.StandardSizes;

    public NewIconDialog(Window? owner)
    {
        Owner = owner;
        Title = "New Icon";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["Brush.Panel"];
        NativeChrome.ApplyDarkTitleBar(this);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Include resolutions",
            Style = (Style)Application.Current.Resources["Label.Section"]
        });

        foreach (var s in IconDocument.StandardSizes)
        {
            var cb = new CheckBox { Content = $"{s} × {s}", IsChecked = true };
            _boxes[s] = cb;
            panel.Children.Add(cb);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
        cancel.Click += (_, _) => Close();
        var ok = new Button { Content = "Create", MinWidth = 84, IsDefault = true, Style = (Style)Application.Current.Resources["Button.Accent"] };
        ok.Click += (_, _) =>
        {
            var sizes = _boxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToArray();
            if (sizes.Length == 0)
            {
                ModalDialog.Error(this, "No sizes selected", "Choose at least one resolution.");
                return;
            }
            SelectedSizes = sizes;
            _ok = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);

        Content = panel;
    }

    public static int[]? Show(Window? owner)
    {
        var dlg = new NewIconDialog(owner);
        dlg.ShowDialog();
        return dlg._ok ? dlg.SelectedSizes : null;
    }
}
