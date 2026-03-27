using System.Collections.ObjectModel;
using Avalonia.Media;
using Codiff.Models;
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
