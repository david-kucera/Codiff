using System.Diagnostics;

namespace Codiff.Services;

/// <summary>
/// Opens folders and reveals files in the platform's file manager (Explorer, Finder, xdg-open).
/// Failures are ignored: these are convenience actions.
/// </summary>
public static class FileManagerService
{
    /// <summary>Opens <paramref name="folderPath"/> in the file manager.</summary>
    public static void OpenFolder(string folderPath)
    {
        if (OperatingSystem.IsWindows())
            StartRaw("explorer.exe", folderPath);
        else if (OperatingSystem.IsMacOS())
            Start("open", folderPath);
        else if (OperatingSystem.IsLinux())
            Start("xdg-open", folderPath);
    }

    /// <summary>
    /// Shows <paramref name="filePath"/> selected in its folder
    /// (on Linux the containing folder is opened, as there is no standard way to select a file).
    /// </summary>
    public static void RevealFile(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            // explorer.exe parses its own command line, so the argument string is passed as-is.
            StartRaw("explorer.exe", $"/select,\"{filePath}\"");
        }
        else if (OperatingSystem.IsMacOS())
        {
            Start("open", "-R", filePath);
        }
        else if (OperatingSystem.IsLinux())
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null)
                Start("xdg-open", dir);
        }
    }

    /// <summary>Starts a process with each argument passed separately (paths may contain spaces or quotes).</summary>
    private static void Start(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        Run(startInfo);
    }

    private static void StartRaw(string fileName, string arguments) =>
        Run(new ProcessStartInfo(fileName, arguments));

    private static void Run(ProcessStartInfo startInfo)
    {
        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FileManagerService] Failed to start '{startInfo.FileName}': {ex.Message}");
        }
    }
}
