using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Media.Imaging;

namespace Codiff.Services;

/// <summary>
/// Provides functionality to extract and cache file and folder icons from the Windows operating system.
/// </summary>
/// <remarks>This class is intended for use on Windows platforms and is not supported on other operating systems.
/// It implements a singleton pattern accessible via the Instance property. Icons are cached to improve performance and
/// reduce redundant extraction operations. All extracted icons are returned as Bitmap objects. Callers should dispose
/// of any returned Bitmap when it is no longer needed. Use the ClearCache method to release all cached icons and free
/// associated resources.</remarks>
[SupportedOSPlatform("windows")]
public class IconExtractorService
{
    #region Constants
    private const string DefaultFolderIconKey = "folder";
    private const string DefaultFileIconKey = "file";

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, uint nIconIndex);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    #endregion // Constants

    #region Class members
    private static IconExtractorService? _instance;
    private static readonly object _lock = new();
    private static readonly Dictionary<string, Bitmap?> IconCache = new();
    #endregion // Class members

    #region Public functions
    public static IconExtractorService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new IconExtractorService();
                }
            }
            return _instance;
        }
    }

    public Bitmap? GetFileIcon(string filePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            var cacheKey = GetCacheKey(filePath, isDirectory: false);
            if (IconCache.TryGetValue(cacheKey, out var cachedIcon))
                return cachedIcon;

            var bitmap = ExtractIconForPath(filePath, isDirectory: false);
            IconCache[cacheKey] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public Bitmap? GetFolderIcon(string folderPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            var cacheKey = DefaultFolderIconKey;
            if (IconCache.TryGetValue(cacheKey, out var cachedIcon))
                return cachedIcon;

            var bitmap = ExtractGenericFolderIcon();
            IconCache[cacheKey] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Folder icon error: {ex}");
            return null;
        }
    }

    public void ClearCache()
    {
        foreach (var icon in IconCache.Values)
        {
            icon?.Dispose();
        }
        IconCache.Clear();
    }
    #endregion // Public functions

    #region Private functions
    private static Bitmap? ExtractGenericFolderIcon()
    {
        try
        {
            var shfi = new SHFILEINFO();
            var flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
            uint fileAttributes = FILE_ATTRIBUTE_DIRECTORY;

            if (SHGetFileInfo("C:\\FakeFolder", fileAttributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags) == 0)
                return null;

            if (shfi.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var bitmap = ConvertIconToBitmap(shfi.hIcon);
                return bitmap;
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Generic folder icon error: {ex}");
            return null;
        }
    }

    private static Bitmap? ExtractIconForPath(string path, bool isDirectory)
    {
        var shfi = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_SMALLICON;

        if (isDirectory)
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        uint fileAttributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : 0;

        if (SHGetFileInfo(path, fileAttributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags) == 0)
            return null;

        if (shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var bitmap = ConvertIconToBitmap(shfi.hIcon);
            return bitmap;
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }


    private static Bitmap? ConvertIconToBitmap(IntPtr hIcon)
    {
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(hIcon);
            int width = icon.Width > 0 ? icon.Width : 16;
            int height = icon.Height > 0 ? icon.Height : 16;

            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.DrawIcon(icon, 0, 0);
            }

            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Seek(0, SeekOrigin.Begin);

            byte[] imageData = ms.ToArray();
            var memoryStream = new MemoryStream(imageData);

            return new Bitmap(memoryStream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Bitmap conversion error: {ex}");
            return null;
        }
    }

    private static string GetCacheKey(string path, bool isDirectory)
    {
        if (isDirectory)
            return DefaultFolderIconKey;

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return string.IsNullOrEmpty(extension) ? DefaultFileIconKey : $"file{extension}";
    }
    #endregion // Private functions
}