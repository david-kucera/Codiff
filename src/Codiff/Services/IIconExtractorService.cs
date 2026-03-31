using Avalonia.Media.Imaging;

namespace Codiff.Services;

/// <summary>
/// Platform-agnostic interface for extracting and caching file and folder icons.
/// </summary>
public interface IIconExtractorService
{
    /// <summary>Returns a small icon bitmap for the given file path, or <see langword="null"/> if unavailable.</summary>
    Bitmap? GetFileIcon(string filePath);

    /// <summary>Returns a small icon bitmap for a folder, or <see langword="null"/> if unavailable.</summary>
    Bitmap? GetFolderIcon(string folderPath);

    /// <summary>Disposes and clears all cached icon bitmaps.</summary>
    void ClearCache();
}
