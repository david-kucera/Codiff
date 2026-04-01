using Avalonia;
using Avalonia.Styling;

namespace Codiff.Services;

/// <summary>
/// Singleton service for managing the application's light/dark theme.
/// </summary>
public class ThemeService
{
    public static readonly ThemeService Instance = new();

    private ThemeService() { }

    /// <summary>Raised after the theme has been changed.</summary>
    public event Action? ThemeChanged;

    private bool _isDark;

    /// <summary>True when the dark theme is active.</summary>
    public bool IsDark
    {
        get => _isDark;
        private set
        {
            if (_isDark == value) return;
            _isDark = value;
            if (Application.Current is { } app)
                app.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
            ConfigService.Instance.Config.IsDarkTheme = _isDark;
            ConfigService.Instance.Save();
            ThemeChanged?.Invoke();
        }
    }

    /// <summary>
    /// Applies the given theme at startup without raising <see cref="ThemeChanged"/>.
    /// Must be called after <see cref="Application.Current"/> is available.
    /// </summary>
    public void Initialize(bool isDark)
    {
        _isDark = isDark;
        if (Application.Current is { } app)
            app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    /// <summary>Toggles between dark and light theme.</summary>
    public void Toggle() => IsDark = !IsDark;
}
