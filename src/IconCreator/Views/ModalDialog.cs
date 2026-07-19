using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IconCreator.Views;

/// <summary>
/// Themed modal popup used everywhere in place of MessageBox / alert().
/// Provides Info, Error, and Confirm helpers.
/// </summary>
public sealed class ModalDialog : Window
{
    private bool _result;

    private ModalDialog(Window? owner, string title, string message,
                        string accentColorKey, string primaryText, string? secondaryText)
    {
        Owner = owner;
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height;
        Width = 420;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["Brush.Panel"];
        NativeChrome.ApplyDarkTitleBar(this);

        var accent = (Brush)Application.Current.Resources[accentColorKey];

        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
        root.RowDefinitions.Add(new RowDefinition());

        var stripe = new Border { Background = accent };
        Grid.SetRow(stripe, 0);
        root.Children.Add(stripe);

        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        Grid.SetRow(body, 1);

        body.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["Brush.Text"],
            Margin = new Thickness(0, 0, 0, 8)
        });

        body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["Brush.TextDim"],
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 20)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (secondaryText != null)
        {
            var cancel = new Button
            {
                Content = secondaryText,
                MinWidth = 90,
                Margin = new Thickness(0, 0, 10, 0),
                IsCancel = true
            };
            cancel.Click += (_, _) => { _result = false; Close(); };
            buttons.Children.Add(cancel);
        }

        var ok = new Button
        {
            Content = primaryText,
            MinWidth = 96,
            IsDefault = true,
            Style = (Style)Application.Current.Resources["Button.Accent"]
        };
        ok.Click += (_, _) => { _result = true; Close(); };
        buttons.Children.Add(ok);

        body.Children.Add(buttons);
        root.Children.Add(body);
        Content = root;
    }

    public static void Info(Window? owner, string title, string message) =>
        new ModalDialog(owner, title, message, "Brush.Accent", "OK", null).ShowDialog();

    public static void Error(Window? owner, string title, string message) =>
        new ModalDialog(owner, title, message, "Brush.Danger", "OK", null).ShowDialog();

    public static bool Confirm(Window? owner, string title, string message,
                               string primary = "OK", string secondary = "Cancel")
    {
        var dlg = new ModalDialog(owner, title, message, "Brush.Warning", primary, secondary);
        dlg.ShowDialog();
        return dlg._result;
    }
}
