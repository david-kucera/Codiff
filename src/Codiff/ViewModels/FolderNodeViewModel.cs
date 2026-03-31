using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Codiff.Models;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

/// <summary>
/// Indicates which side of the diff a node belongs to.
/// </summary>
public enum FolderSide
{
    Left,
    Right
}

/// <summary>
/// Represents a node (folder or file) in the folder diff tree.
/// Nodes only exist for items that differ between folders.
/// </summary>
public partial class FolderNodeViewModel : ObservableObject
{
    private static readonly IBrush ModifiedBrushLight = new SolidColorBrush(Color.Parse("#FFFACD"));
    private static readonly IBrush PresentBrushLight = new SolidColorBrush(Color.Parse("#CCFFCC"));
    private static readonly IBrush MissingBrushLight = new SolidColorBrush(Color.Parse("#FFCCCC"));

    private static readonly IBrush ModifiedBrushDark = new SolidColorBrush(Color.Parse("#3A3000"));
    private static readonly IBrush PresentBrushDark = new SolidColorBrush(Color.Parse("#003000"));
    private static readonly IBrush MissingBrushDark = new SolidColorBrush(Color.Parse("#3A0000"));

    private static IBrush ModifiedBrush => ThemeService.Instance.IsDark ? ModifiedBrushDark : ModifiedBrushLight;
    private static IBrush PresentBrush => ThemeService.Instance.IsDark ? PresentBrushDark : PresentBrushLight;
    private static IBrush MissingBrush => ThemeService.Instance.IsDark ? MissingBrushDark : MissingBrushLight;

    private enum NodeDisplayStatus
    {
        None,
        Modified,
        PresentOnlyOnThisSide,
        MissingOnThisSide
    }

    public string? LeftFullPath { get; }
    public string? RightFullPath { get; }

    public string RelativePath { get; }
    public FileStatus Status { get; }

    public bool IsFile { get; }

    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    public FolderNodeViewModel? Parent { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandSymbol))]
    private bool _isExpanded;

    [ObservableProperty]
    private Bitmap? _leftIcon;

    [ObservableProperty]
    private Bitmap? _rightIcon;

    [ObservableProperty]
    private string _leftSize = "";

    [ObservableProperty]
    private string _rightSize = "";

    [ObservableProperty]
    private string _leftType = "";

    [ObservableProperty]
    private string _rightType = "";

    [ObservableProperty]
    private string _leftTime = "";

    [ObservableProperty]
    private string _rightTime = "";

    /// <summary>Raw byte count used for sorting by Size (<see langword="null"/> when not present on this side).</summary>
    public long? LeftSizeBytes { get; private set; }

    /// <summary>Raw byte count used for sorting by Size (<see langword="null"/> when not present on this side).</summary>
    public long? RightSizeBytes { get; private set; }

    /// <summary>Raw last-write timestamp used for sorting by Time (<see langword="null"/> when not present on this side).</summary>
    public DateTime? LeftTimeRaw { get; private set; }

    /// <summary>Raw last-write timestamp used for sorting by Time (<see langword="null"/> when not present on this side).</summary>
    public DateTime? RightTimeRaw { get; private set; }

    public string LeftStatusIcon => GetStatusIcon(FolderSide.Left);

    public string RightStatusIcon => GetStatusIcon(FolderSide.Right);

    public IBrush LeftStatusColor => GetStatusColor(FolderSide.Left);

    public IBrush RightStatusColor => GetStatusColor(FolderSide.Right);

    /// <summary>Tree depth (0 = root). Set by FolderDiffViewModel when building flat rows.</summary>
    public int Depth { get; set; }

    /// <summary>Left-margin width in pixels used to indent the Name cell.</summary>
    public double IndentWidth => Depth * 16.0;

    /// <summary>Triangle symbol shown in the expand/collapse toggle button.</summary>
    public string ExpandSymbol => IsExpandable ? (IsExpanded ? "▼" : "▶") : string.Empty;

    /// <summary>True for folder nodes that have at least one child.</summary>
    public bool IsExpandable => !IsFile && Children.Count > 0;

    public string LeftName => !string.IsNullOrEmpty(LeftFullPath)
        ? System.IO.Path.GetFileName(LeftFullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
        : System.IO.Path.GetFileName(RelativePath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    public string RightName => !string.IsNullOrEmpty(RightFullPath)
        ? System.IO.Path.GetFileName(RightFullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
        : System.IO.Path.GetFileName(RelativePath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    [RelayCommand]
    private void ToggleExpanded()
    {
        if (IsExpandable)
            IsExpanded = !IsExpanded;
    }

    /// <summary>Notifies the view that all brush-bound properties have changed (called on theme toggle).</summary>
    public void NotifyThemeChanged()
    {
        OnPropertyChanged(nameof(LeftStatusColor));
        OnPropertyChanged(nameof(RightStatusColor));
    }

    public FolderNodeViewModel(
        string relativePath,
        FileStatus status,
        string? leftFullPath,
        string? rightFullPath,
        bool isFile,
        FolderNodeViewModel? parent)
    {
        RelativePath = relativePath;
        Status = status;
        LeftFullPath = leftFullPath;
        RightFullPath = rightFullPath;
        IsFile = isFile;
        Parent = parent;

        LoadIcons();
        LoadFileInfo();
    }

    private void LoadIcons()
    {
        var iconService = IconExtractorServiceFactory.Instance;

        // Load icons asynchronously to avoid blocking the UI
        Task.Run(() =>
        {
            // For files, use the actual path; for folders, use generic icon even if path is null
            if (IsFile && !string.IsNullOrEmpty(LeftFullPath))
            {
                var icon = iconService.GetFileIcon(LeftFullPath);
                if (icon != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => LeftIcon = icon);
                }
            }
            else if (!IsFile)
            {
                // For folders, always load generic folder icon
                var icon = iconService.GetFolderIcon(LeftFullPath ?? "");
                if (icon != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => LeftIcon = icon);
                }
            }

            if (IsFile && !string.IsNullOrEmpty(RightFullPath))
            {
                var icon = iconService.GetFileIcon(RightFullPath);
                if (icon != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => RightIcon = icon);
                }
            }
            else if (!IsFile)
            {
                // For folders, always load generic folder icon
                var icon = iconService.GetFolderIcon(RightFullPath ?? "");
                if (icon != null)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => RightIcon = icon);
                }
            }
        });
    }

    private void LoadFileInfo()
    {
        // Load file info asynchronously
        Task.Run(() =>
        {
            if (!string.IsNullOrEmpty(LeftFullPath))
            {
                try
                {
                    if (IsFile && File.Exists(LeftFullPath))
                    {
                        var fileInfo = new FileInfo(LeftFullPath);
                        var bytes = fileInfo.Length;
                        var time = fileInfo.LastWriteTime;
                        LeftSizeBytes = bytes;
                        LeftTimeRaw = time;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            LeftSize = FormatSize(bytes);
                            LeftType = GetFileType(LeftFullPath);
                            LeftTime = time.ToString("dd-MMM-yy HH:mm:ss");
                        });
                    }
                    else if (!IsFile && Directory.Exists(LeftFullPath))
                    {
                        var dirInfo = new DirectoryInfo(LeftFullPath);
                        var time = dirInfo.LastWriteTime;
                        LeftTimeRaw = time;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            LeftSize = "";
                            LeftType = "File folder";
                            LeftTime = time.ToString("dd-MMM-yy HH:mm:ss");
                        });
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(RightFullPath))
            {
                try
                {
                    if (IsFile && File.Exists(RightFullPath))
                    {
                        var fileInfo = new FileInfo(RightFullPath);
                        var bytes = fileInfo.Length;
                        var time = fileInfo.LastWriteTime;
                        RightSizeBytes = bytes;
                        RightTimeRaw = time;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            RightSize = FormatSize(bytes);
                            RightType = GetFileType(RightFullPath);
                            RightTime = time.ToString("dd-MMM-yy HH:mm:ss");
                        });
                    }
                    else if (!IsFile && Directory.Exists(RightFullPath))
                    {
                        var dirInfo = new DirectoryInfo(RightFullPath);
                        var time = dirInfo.LastWriteTime;
                        RightTimeRaw = time;
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            RightSize = "";
                            RightType = "File folder";
                            RightTime = time.ToString("dd-MMM-yy HH:mm:ss");
                        });
                    }
                }
                catch { }
            }
        });
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static string GetFileType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".exe" => "Application",
            ".dll" => "Dynamic Link Library",
            ".txt" => "Text Document",
            ".xml" => "XML Document",
            ".config" => "Configuration File",
            ".cs" => "C# Source File",
            ".csproj" => "C# Project File",
            ".sln" => "Visual Studio Solution",
            ".json" => "JSON Document",
            _ => $"{extension.TrimStart('.')} File"
        };
    }

    private string GetStatusIcon(FolderSide side) => GetDisplayStatus(side) switch
    {
        NodeDisplayStatus.Modified => "~",
        NodeDisplayStatus.PresentOnlyOnThisSide => "+",
        NodeDisplayStatus.MissingOnThisSide => "-",
        _ => string.Empty
    };

    private IBrush GetStatusColor(FolderSide side) => GetDisplayStatus(side) switch
    {
        NodeDisplayStatus.Modified => ModifiedBrush,
        NodeDisplayStatus.PresentOnlyOnThisSide => PresentBrush,
        NodeDisplayStatus.MissingOnThisSide => MissingBrush,
        _ => Brushes.Transparent
    };

    private NodeDisplayStatus GetDisplayStatus(FolderSide side)
    {
        if (IsFile)
        {
            return Status switch
            {
                FileStatus.Modified => NodeDisplayStatus.Modified,
                FileStatus.OnlyInLeft when side == FolderSide.Left => NodeDisplayStatus.PresentOnlyOnThisSide,
                FileStatus.OnlyInRight when side == FolderSide.Right => NodeDisplayStatus.PresentOnlyOnThisSide,
                FileStatus.OnlyInLeft or FileStatus.OnlyInRight => NodeDisplayStatus.MissingOnThisSide,
                _ => NodeDisplayStatus.None
            };
        }

        if (Children.Count == 0)
            return NodeDisplayStatus.None;

        bool hasModified = false;
        bool hasPresentOnly = false;
        bool hasMissing = false;

        foreach (var child in Children)
        {
            switch (child.GetDisplayStatus(side))
            {
                case NodeDisplayStatus.Modified:
                    hasModified = true;
                    break;
                case NodeDisplayStatus.PresentOnlyOnThisSide:
                    hasPresentOnly = true;
                    break;
                case NodeDisplayStatus.MissingOnThisSide:
                    hasMissing = true;
                    break;
            }

            if (hasModified && hasPresentOnly && hasMissing)
                break;
        }

        if (hasModified)
            return NodeDisplayStatus.Modified;

        if (hasPresentOnly && hasMissing)
            return NodeDisplayStatus.Modified;

        if (hasPresentOnly)
            return NodeDisplayStatus.PresentOnlyOnThisSide;

        if (hasMissing)
            return NodeDisplayStatus.MissingOnThisSide;

        return NodeDisplayStatus.None;
    }
}
