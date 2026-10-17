using System.ComponentModel;
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

    // Reused for theme notifications, which are raised for every visible row at once.
    private static readonly PropertyChangedEventArgs LeftStatusColorChangedArgs = new(nameof(LeftStatusColor));
    private static readonly PropertyChangedEventArgs RightStatusColorChangedArgs = new(nameof(RightStatusColor));

    private const string TimeFormat = "dd-MMM-yy HH:mm:ss";
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB" };

    private enum NodeDisplayStatus
    {
        None,
        Modified,
        PresentOnlyOnThisSide,
        MissingOnThisSide
    }

    private readonly List<FolderNodeViewModel> _children = [];

    // Aggregated status of a folder's subtree, computed on first use (rows are re-bound often
    // while scrolling, and walking the whole subtree each time is expensive for big trees).
    private NodeDisplayStatus? _leftDisplayStatus;
    private NodeDisplayStatus? _rightDisplayStatus;

    public string? LeftFullPath { get; }
    public string? RightFullPath { get; }

    public string RelativePath { get; }
    public FileStatus Status { get; }

    public bool IsFile { get; }

    /// <summary>Child nodes; only added to while the tree is being built (see <see cref="AddChild"/>).</summary>
    public IReadOnlyList<FolderNodeViewModel> Children => _children;

    public FolderNodeViewModel? Parent { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandSymbol))]
    private bool _isExpanded;

    public Bitmap? LeftIcon { get; }

    public Bitmap? RightIcon { get; }

    public string LeftSize => LeftSizeBytes is { } bytes ? FormatSize(bytes) : string.Empty;

    public string RightSize => RightSizeBytes is { } bytes ? FormatSize(bytes) : string.Empty;

    public string LeftType => IsFile && !string.IsNullOrEmpty(LeftFullPath) ? GetFileType(LeftFullPath) : string.Empty;

    public string RightType => IsFile && !string.IsNullOrEmpty(RightFullPath) ? GetFileType(RightFullPath) : string.Empty;

    public string LeftTime => LeftTimeRaw?.ToString(TimeFormat) ?? string.Empty;

    public string RightTime => RightTimeRaw?.ToString(TimeFormat) ?? string.Empty;

    /// <summary>Raw byte count used for sorting by Size (<see langword="null"/> when not present on this side).</summary>
    public long? LeftSizeBytes { get; }

    /// <summary>Raw byte count used for sorting by Size (<see langword="null"/> when not present on this side).</summary>
    public long? RightSizeBytes { get; }

    /// <summary>Raw last-write timestamp used for sorting by Time (<see langword="null"/> when not present on this side).</summary>
    public DateTime? LeftTimeRaw { get; }

    /// <summary>Raw last-write timestamp used for sorting by Time (<see langword="null"/> when not present on this side).</summary>
    public DateTime? RightTimeRaw { get; }

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

    public string LeftName { get; }

    public string RightName { get; }

    [RelayCommand]
    private void ToggleExpanded()
    {
        if (IsExpandable)
            IsExpanded = !IsExpanded;
    }

    /// <summary>Notifies the view that all brush-bound properties have changed (called on theme toggle).</summary>
    public void NotifyThemeChanged()
    {
        OnPropertyChanged(LeftStatusColorChangedArgs);
        OnPropertyChanged(RightStatusColorChangedArgs);
    }

    /// <summary>Creates a folder node (or a file node without scan metadata).</summary>
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

        LeftName = GetDisplayName(leftFullPath, relativePath);
        RightName = GetDisplayName(rightFullPath, relativePath);

        (LeftIcon, RightIcon) = LoadIcons();
    }

    /// <summary>Creates a file node for a folder-diff entry; size and time come from the folder scan.</summary>
    public FolderNodeViewModel(FolderDiffEntry entry, string relativePath, FolderNodeViewModel? parent)
        : this(relativePath, entry.Status, entry.LeftFullPath, entry.RightFullPath, isFile: true, parent)
    {
        LeftSizeBytes = entry.LeftSize;
        RightSizeBytes = entry.RightSize;
        LeftTimeRaw = entry.LeftLastWriteTime;
        RightTimeRaw = entry.RightLastWriteTime;
    }

    /// <summary>Adds a child node while building the tree.</summary>
    internal void AddChild(FolderNodeViewModel child)
    {
        _children.Add(child);

        // The aggregated status of this folder and its ancestors may have changed.
        for (var node = this; node != null; node = node.Parent)
        {
            node._leftDisplayStatus = null;
            node._rightDisplayStatus = null;
        }
    }

    private static string GetDisplayName(string? fullPath, string relativePath)
    {
        var path = !string.IsNullOrEmpty(fullPath) ? fullPath : relativePath;
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private (Bitmap? Left, Bitmap? Right) LoadIcons()
    {
        var iconService = IconExtractorServiceFactory.Instance;

        if (!IsFile)
            return (iconService.GetFolderIcon(LeftFullPath ?? ""), iconService.GetFolderIcon(RightFullPath ?? ""));

        // File icons are cached per extension, and FolderDiffViewModel warms that cache on a
        // background thread before it builds the tree, so these lookups are cheap cache hits.
        return (
            string.IsNullOrEmpty(LeftFullPath) ? null : iconService.GetFileIcon(LeftFullPath),
            string.IsNullOrEmpty(RightFullPath) ? null : iconService.GetFileIcon(RightFullPath));
    }

    private static string FormatSize(long bytes)
    {
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < SizeUnits.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {SizeUnits[order]}";
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
            "" => "File",
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

    private NodeDisplayStatus GetDisplayStatus(FolderSide side) => side == FolderSide.Left
        ? _leftDisplayStatus ??= ComputeDisplayStatus(side)
        : _rightDisplayStatus ??= ComputeDisplayStatus(side);

    private NodeDisplayStatus ComputeDisplayStatus(FolderSide side)
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
