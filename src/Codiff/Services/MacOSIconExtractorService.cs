using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Media.Imaging;

namespace Codiff.Services;

/// <summary>
/// Provides functionality to extract and cache file and folder icons on macOS
/// by calling the AppKit NSWorkspace API via the Objective-C runtime.
/// </summary>
[SupportedOSPlatform("macos")]
public class MacOSIconExtractorService : IIconExtractorService
{
    #region ObjC runtime P/Invoke

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr GetSel(string name);

    // objc_msgSend variants – same native symbol, different managed signatures
    // so the P/Invoke layer marshals arguments correctly for each call site.

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend(IntPtr obj, IntPtr sel);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSendPtr(IntPtr obj, IntPtr sel, IntPtr arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void MsgSendSetSize(IntPtr obj, IntPtr sel, NSSize size);

    // representationUsingType:properties: – first arg is NSBitmapImageFileType (nuint), second is NSDictionary* (or nil)
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSendRepresentation(IntPtr obj, IntPtr sel, nuint type, IntPtr props);

    // -[NSData length] returns NSUInteger
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nuint MsgSendLength(IntPtr obj, IntPtr sel);

    [StructLayout(LayoutKind.Sequential)]
    private struct NSSize
    {
        public double Width;
        public double Height;
    }

    #endregion

    #region Selectors / cached state

    private static MacOSIconExtractorService? _instance;
    private static readonly object _lock = new();
    private static readonly Dictionary<string, Bitmap?> _iconCache = new();

    private readonly bool _initialized;
    private readonly IntPtr _sharedWorkspace;
    private readonly IntPtr _iconForFileSel;
    private readonly IntPtr _setSizeSel;
    private readonly IntPtr _tiffRepresentationSel;
    private readonly IntPtr _nsBitmapImageRepClass;
    private readonly IntPtr _imageRepWithDataSel;
    private readonly IntPtr _representationUsingTypeSel;
    private readonly IntPtr _bytesSel;
    private readonly IntPtr _lengthSel;
    private readonly IntPtr _nsAutoreleasePoolClass;
    private readonly IntPtr _allocSel;
    private readonly IntPtr _drainSel;
    private readonly IntPtr _nsStringClass;
    private readonly IntPtr _initWithUTF8StringSel;
    private readonly IntPtr _releaseSel;

    #endregion

    private const string DefaultFolderIconKey = "folder";
    private const string DefaultFileIconKey = "file";

    public static MacOSIconExtractorService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock) { _instance ??= new MacOSIconExtractorService(); }
            }
            return _instance;
        }
    }

    private MacOSIconExtractorService()
    {
        try
        {
            _allocSel = GetSel("alloc");
            _drainSel = GetSel("drain");
            _releaseSel = GetSel("release");
            _nsAutoreleasePoolClass = GetClass("NSAutoreleasePool");
            _nsStringClass = GetClass("NSString");
            _initWithUTF8StringSel = GetSel("initWithUTF8String:");

            var nsWorkspaceClass = GetClass("NSWorkspace");
            var sharedWorkspaceSel = GetSel("sharedWorkspace");
            _sharedWorkspace = MsgSend(nsWorkspaceClass, sharedWorkspaceSel);

            _iconForFileSel = GetSel("iconForFile:");
            _setSizeSel = GetSel("setSize:");
            _tiffRepresentationSel = GetSel("TIFFRepresentation");
            _nsBitmapImageRepClass = GetClass("NSBitmapImageRep");
            _imageRepWithDataSel = GetSel("imageRepWithData:");
            _representationUsingTypeSel = GetSel("representationUsingType:properties:");
            _bytesSel = GetSel("bytes");
            _lengthSel = GetSel("length");

            _initialized = _sharedWorkspace != IntPtr.Zero;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MacOSIconExtractorService init error: {ex}");
            _initialized = false;
        }
    }

    public Bitmap? GetFileIcon(string filePath)
    {
        if (!_initialized) return null;
        try
        {
            var cacheKey = GetCacheKey(filePath, isDirectory: false);
            if (_iconCache.TryGetValue(cacheKey, out var cached)) return cached;
            var bitmap = ExtractIcon(filePath);
            _iconCache[cacheKey] = bitmap;
            return bitmap;
        }
        catch { return null; }
    }

    public Bitmap? GetFolderIcon(string folderPath)
    {
        if (!_initialized) return null;
        try
        {
            if (_iconCache.TryGetValue(DefaultFolderIconKey, out var cached)) return cached;
            // Use the actual path if available, otherwise root – NSWorkspace always returns a folder icon
            var path = string.IsNullOrEmpty(folderPath) ? "/" : folderPath;
            var bitmap = ExtractIcon(path);
            _iconCache[DefaultFolderIconKey] = bitmap;
            return bitmap;
        }
        catch { return null; }
    }

    public void ClearCache()
    {
        foreach (var icon in _iconCache.Values) icon?.Dispose();
        _iconCache.Clear();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private Bitmap? ExtractIcon(string path)
    {
        // Wrap in an autorelease pool so ObjC autoreleased objects are freed promptly.
        var pool = MsgSend(MsgSend(_nsAutoreleasePoolClass, _allocSel), GetSel("init"));
        try
        {
            var nsPath = CreateNSString(path);
            if (nsPath == IntPtr.Zero) return null;
            try
            {
                // [[NSWorkspace sharedWorkspace] iconForFile:path]
                var nsImage = MsgSendPtr(_sharedWorkspace, _iconForFileSel, nsPath);
                if (nsImage == IntPtr.Zero) return null;

                // [image setSize:NSMakeSize(16,16)]  – keeps the TIFF/PNG small
                MsgSendSetSize(nsImage, _setSizeSel, new NSSize { Width = 16.0, Height = 16.0 });

                // [image TIFFRepresentation]
                var tiffData = MsgSend(nsImage, _tiffRepresentationSel);
                if (tiffData == IntPtr.Zero) return null;

                // [NSBitmapImageRep imageRepWithData:tiffData]
                var bitmapRep = MsgSendPtr(_nsBitmapImageRepClass, _imageRepWithDataSel, tiffData);
                if (bitmapRep == IntPtr.Zero) return null;

                // [bitmapRep representationUsingType:NSBitmapImageFileTypePNG properties:nil]
                // NSBitmapImageFileTypePNG = 4
                var pngData = MsgSendRepresentation(bitmapRep, _representationUsingTypeSel, 4u, IntPtr.Zero);
                if (pngData == IntPtr.Zero) return null;

                var length = MsgSendLength(pngData, _lengthSel);
                if (length == 0) return null;

                var bytesPtr = MsgSend(pngData, _bytesSel);
                if (bytesPtr == IntPtr.Zero) return null;

                var bytes = new byte[(int)length];
                Marshal.Copy(bytesPtr, bytes, 0, (int)length);

                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }
            finally
            {
                MsgSend(nsPath, _releaseSel);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MacOSIconExtractorService icon error for '{path}': {ex}");
            return null;
        }
        finally
        {
            MsgSend(pool, _drainSel);
        }
    }

    /// <summary>Creates an NSString (alloc+initWithUTF8String:) owned by the caller – must be released.</summary>
    private IntPtr CreateNSString(string s)
    {
        var allocated = MsgSend(_nsStringClass, _allocSel);
        if (allocated == IntPtr.Zero) return IntPtr.Zero;

        var utf8Ptr = Marshal.StringToCoTaskMemUTF8(s);
        try
        {
            return MsgSendPtr(allocated, _initWithUTF8StringSel, utf8Ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8Ptr);
        }
    }

    private static string GetCacheKey(string path, bool isDirectory)
    {
        if (isDirectory) return DefaultFolderIconKey;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? DefaultFileIconKey : $"file{ext}";
    }
}
