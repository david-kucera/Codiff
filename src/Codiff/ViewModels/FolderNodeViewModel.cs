using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Codiff.Models;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;

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
    private static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.Parse("#FFFACD"));
    private static readonly IBrush PresentBrush = new SolidColorBrush(Color.Parse("#CCFFCC"));
    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.Parse("#FFCCCC"));

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

    public string LeftStatusIcon => GetStatusIcon(FolderSide.Left);

    public string RightStatusIcon => GetStatusIcon(FolderSide.Right);

    public IBrush LeftStatusColor => GetStatusColor(FolderSide.Left);

    public IBrush RightStatusColor => GetStatusColor(FolderSide.Right);

    public string LeftName => !string.IsNullOrEmpty(LeftFullPath)
        ? System.IO.Path.GetFileName(LeftFullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
        : System.IO.Path.GetFileName(RelativePath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    public string RightName => !string.IsNullOrEmpty(RightFullPath)
        ? System.IO.Path.GetFileName(RightFullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
        : System.IO.Path.GetFileName(RelativePath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

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
        var iconService = IconExtractorService.Instance;

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
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            LeftSize = FormatSize(fileInfo.Length);
                            LeftType = GetFileType(LeftFullPath);
                            LeftTime = fileInfo.LastWriteTime.ToString("dd-MMM-yy HH:mm:ss");
                        });
                    }
                    else if (!IsFile && Directory.Exists(LeftFullPath))
                    {
                        var dirInfo = new DirectoryInfo(LeftFullPath);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            LeftSize = "";
                            LeftType = "File folder";
                            LeftTime = dirInfo.LastWriteTime.ToString("dd-MMM-yy HH:mm:ss");
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
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            RightSize = FormatSize(fileInfo.Length);
                            RightType = GetFileType(RightFullPath);
                            RightTime = fileInfo.LastWriteTime.ToString("dd-MMM-yy HH:mm:ss");
                        });
                    }
                    else if (!IsFile && Directory.Exists(RightFullPath))
                    {
                        var dirInfo = new DirectoryInfo(RightFullPath);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            RightSize = "";
                            RightType = "File folder";
                            RightTime = dirInfo.LastWriteTime.ToString("dd-MMM-yy HH:mm:ss");
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
