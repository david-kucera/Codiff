using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Codiff.Views;

/// <summary>A simple modal message box with an OK button.</summary>
public static class MessageDialog
{
    public static async Task ShowAsync(Window owner, string title, string message)
    {
        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsDefault = true
        };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
                    okButton
                }
            }
        };

        okButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }
}
