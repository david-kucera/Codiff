using Avalonia.Media;
using Codiff.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Codiff.ViewModels;

public partial class FolderEntryViewModel : ObservableObject
{
    public FolderDiffEntry Model { get; }

    public string RelativePath => Model.RelativePath;
    public string StatusIcon => Model.StatusIcon;
    public string? LeftFullPath => Model.LeftFullPath;
    public string? RightFullPath => Model.RightFullPath;
    public FileStatus Status => Model.Status;

    public IBrush StatusColor => Model.Status switch
    {
        FileStatus.Modified => new SolidColorBrush(Color.Parse("#FFFACD")),
        FileStatus.OnlyInLeft => new SolidColorBrush(Color.Parse("#FFCCCC")),
        FileStatus.OnlyInRight => new SolidColorBrush(Color.Parse("#CCFFCC")),
        _ => Brushes.Transparent
    };

    public FolderEntryViewModel(FolderDiffEntry model)
    {
        Model = model;
    }
}
