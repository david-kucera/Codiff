using System.Runtime.InteropServices;

namespace Codiff.Services;

/// <summary>
/// Creates the platform-appropriate <see cref="IIconExtractorService"/> implementation.
/// </summary>
public static class IconExtractorServiceFactory
{
    private static IIconExtractorService? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Returns the singleton <see cref="IIconExtractorService"/> for the current operating system.
    /// On platforms where no native icon extraction is available the returned service always
    /// returns <see langword="null"/> icons (graceful degradation).
    /// </summary>
    public static IIconExtractorService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock) { _instance ??= Create(); }
            }
            return _instance;
        }
    }

    private static IIconExtractorService Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsIconExtractorService.Instance;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return MacOSIconExtractorService.Instance;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return LinuxIconExtractorService.Instance;

        // Any other platform (FreeBSD, etc.) – return a no-op service.
        return NullIconExtractorService.Instance;
    }
}

/// <summary>
/// No-op implementation used on platforms where icon extraction is not supported.
/// Always returns <see langword="null"/> for every request.
/// </summary>
file sealed class NullIconExtractorService : IIconExtractorService
{
    public static readonly NullIconExtractorService Instance = new();
    private NullIconExtractorService() { }

    public Avalonia.Media.Imaging.Bitmap? GetFileIcon(string filePath) => null;
    public Avalonia.Media.Imaging.Bitmap? GetFolderIcon(string folderPath) => null;
    public void ClearCache() { }
}
