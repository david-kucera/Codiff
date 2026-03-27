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
    public string? LeftFullPath { get; }
    public string? RightFullPath { get; }

    public string RelativePath { get; }
    public FileStatus Status { get; }

    public bool IsFile { get; }

    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    // Side and parent are used to synchronize folder expansion between the two trees.
    public FolderSide Side { get; }

    public FolderNodeViewModel? Parent { get; }

    public string StatusIcon => Status switch
    {
        FileStatus.Modified => "~",
        FileStatus.OnlyInLeft => "-",
        FileStatus.OnlyInRight => "+",
        _ => string.Empty
    };

    public string StatusText => Status switch
    {
        FileStatus.Modified => "modified",
        FileStatus.OnlyInLeft => "only left",
        FileStatus.OnlyInRight => "only right",
        _ => string.Empty
    };

    public IBrush StatusColor => Status switch
    {
        FileStatus.Modified => new SolidColorBrush(Color.Parse("#FFFACD")),
        FileStatus.OnlyInLeft => new SolidColorBrush(Color.Parse("#FFCCCC")),
        FileStatus.OnlyInRight => new SolidColorBrush(Color.Parse("#CCFFCC")),
        _ => Brushes.Transparent
    };

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
        FolderSide side,
        FolderNodeViewModel? parent)
    {
        RelativePath = relativePath;
        Status = status;
        LeftFullPath = leftFullPath;
        RightFullPath = rightFullPath;
        IsFile = isFile;
        Side = side;
        Parent = parent;
    }
}
