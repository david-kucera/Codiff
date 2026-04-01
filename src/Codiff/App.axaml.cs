using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Codiff.Services;
using Codiff.ViewModels;
using Codiff.Views;

namespace Codiff;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Load persisted config (theme + recent comparisons)
        ConfigService.Instance.Load();

        // Apply the saved theme before the window is shown
        ThemeService.Instance.Initialize(ConfigService.Instance.Config.IsDarkTheme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel
            {
                IsDarkTheme = ThemeService.Instance.IsDark
            };
            vm.LoadFromConfig();

            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
