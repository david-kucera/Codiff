using System.Runtime.Versioning;
using Avalonia.Media.Imaging;

namespace Codiff.Services;

/// <summary>
/// Provides functionality to extract and cache file and folder icons on Linux by
/// searching for PNG icons in the freedesktop.org icon theme directories
/// (XDG_DATA_DIRS/icons/&lt;theme&gt;/&lt;size&gt;/&lt;category&gt;/&lt;name&gt;.png).
/// Falls back gracefully to <see langword="null"/> when no icon is found.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxIconExtractorService : IIconExtractorService
{
    private const string DefaultFolderIconKey = "folder";
    private const string DefaultFileIconKey = "file";

    // Preferred themes in lookup order. "hicolor" is the mandatory fallback theme
    // per the freedesktop spec; Adwaita/gnome are the most common desktop themes.
    private static readonly string[] IconThemes = { "hicolor", "Adwaita", "gnome", "breeze", "oxygen" };

    // Sizes to try, from smallest to largest (we prefer 16px for the diff tree).
    private static readonly int[] IconSizes = { 16, 24, 32, 48 };

    // Sub-directories inside <theme>/<size>x<size>/ that may contain relevant icons.
    private static readonly string[] IconCategories = { "mimetypes", "places", "apps" };

    private static LinuxIconExtractorService? _instance;
    private static readonly object _lock = new();

    // Icons are requested from background threads, so the cache is guarded by a lock.
    private static readonly object _cacheLock = new();
    private static readonly Dictionary<string, Bitmap?> _iconCache = new();

    private readonly string[] _searchPaths;

    public static LinuxIconExtractorService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock) { _instance ??= new LinuxIconExtractorService(); }
            }
            return _instance;
        }
    }

    private LinuxIconExtractorService()
    {
        _searchPaths = BuildSearchPaths();
    }

    public Bitmap? GetFileIcon(string filePath)
    {
        try
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var cacheKey = string.IsNullOrEmpty(ext) ? DefaultFileIconKey : $"file{ext}";
            lock (_cacheLock)
            {
                if (_iconCache.TryGetValue(cacheKey, out var cached)) return cached;

                var iconName = GetMimeIconName(ext);
                // Try the specific icon name first, then common generic fallbacks.
                var bitmap = FindIcon(iconName)
                          ?? FindIcon("text-x-generic")
                          ?? FindIcon("application-octet-stream");

                _iconCache[cacheKey] = bitmap;
                return bitmap;
            }
        }
        catch { return null; }
    }

    public Bitmap? GetFolderIcon(string folderPath)
    {
        try
        {
            lock (_cacheLock)
            {
                if (_iconCache.TryGetValue(DefaultFolderIconKey, out var cached)) return cached;
                var bitmap = FindIcon("folder");
                _iconCache[DefaultFolderIconKey] = bitmap;
                return bitmap;
            }
        }
        catch { return null; }
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            foreach (var icon in _iconCache.Values) icon?.Dispose();
            _iconCache.Clear();
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the ordered list of icon base directories according to the
    /// freedesktop.org Base Directory Specification.
    /// </summary>
    private static string[] BuildSearchPaths()
    {
        var paths = new List<string>();

        // $XDG_DATA_HOME (default: ~/.local/share)
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        paths.Add(Path.Combine(xdgDataHome, "icons"));

        // $XDG_DATA_DIRS (default: /usr/local/share:/usr/share)
        var xdgDataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS")
            ?? "/usr/local/share:/usr/share";
        foreach (var dir in xdgDataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            paths.Add(Path.Combine(dir.Trim(), "icons"));
        }

        // Legacy fallback directory
        paths.Add("/usr/share/pixmaps");

        return paths.ToArray();
    }

    /// <summary>
    /// Searches icon theme directories for a PNG icon with the given name and returns
    /// the first Avalonia Bitmap that loads successfully, or <see langword="null"/>.
    /// </summary>
    private Bitmap? FindIcon(string iconName)
    {
        foreach (var basePath in _searchPaths)
        {
            foreach (var theme in IconThemes)
            {
                foreach (var size in IconSizes)
                {
                    foreach (var category in IconCategories)
                    {
                        var path = Path.Combine(basePath, theme, $"{size}x{size}", category, iconName + ".png");
                        if (!File.Exists(path)) continue;
                        try
                        {
                            using var stream = File.OpenRead(path);
                            return new Bitmap(stream);
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LinuxIconExtractorService: failed to load '{path}': {ex.Message}"); }
                    }
                }
            }
        }

        // Also check the legacy /usr/share/pixmaps directory (flat, no theme sub-dirs).
        foreach (var basePath in _searchPaths)
        {
            if (!basePath.EndsWith("pixmaps", StringComparison.Ordinal)) continue;
            var path = Path.Combine(basePath, iconName + ".png");
            if (!File.Exists(path)) continue;
            try
            {
                using var stream = File.OpenRead(path);
                return new Bitmap(stream);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LinuxIconExtractorService: failed to load pixmap '{path}': {ex.Message}"); }
        }

        return null;
    }

    /// <summary>
    /// Maps a lowercase file extension to the freedesktop.org icon name that would
    /// typically represent that file type's MIME category.
    /// </summary>
    private static string GetMimeIconName(string extension) => extension switch
    {
        ".txt" or ".log" or ".md" or ".rst" => "text-plain",
        ".cs"  => "text-x-csharp",
        ".py"  => "text-x-python",
        ".js" or ".mjs" or ".cjs" => "text-x-javascript",
        ".ts"  => "text-x-typescript",
        ".java" => "text-x-java",
        ".cpp" or ".cc" or ".cxx" => "text-x-c++src",
        ".c"   => "text-x-csrc",
        ".h" or ".hpp" => "text-x-chdr",
        ".xml" or ".xaml" or ".axaml" => "text-xml",
        ".html" or ".htm" => "text-html",
        ".css" => "text-css",
        ".json" => "application-json",
        ".sh" or ".bash" or ".zsh" => "text-x-script",
        ".png" => "image-png",
        ".jpg" or ".jpeg" => "image-jpeg",
        ".gif" => "image-gif",
        ".svg" => "image-svg+xml",
        ".pdf" => "application-pdf",
        ".zip" => "application-zip",
        ".tar" or ".gz" or ".bz2" or ".xz" or ".7z" or ".rar" => "application-x-archive",
        ".exe" => "application-x-executable",
        ".dll" or ".so" => "application-x-sharedlib",
        ".sln" or ".csproj" or ".vbproj" or ".fsproj" => "text-x-generic",
        ".config" or ".ini" or ".toml" or ".yaml" or ".yml" => "text-x-generic",
        _ => "text-x-generic"
    };
}
