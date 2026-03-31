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
            ThemeChanged?.Invoke();
        }
    }

    /// <summary>Toggles between dark and light theme.</summary>
    public void Toggle() => IsDark = !IsDark;
}
