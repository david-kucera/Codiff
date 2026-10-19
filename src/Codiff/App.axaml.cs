using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

            // "Codiff <left> <right>" opens a comparison of two files or two folders right away.
            if (desktop.Args is { Length: >= 2 } args)
                Dispatcher.UIThread.Post(() => _ = vm.OpenComparisonAsync(args[0], args[1]), DispatcherPriority.Loaded);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
