using Avalonia.Media;
using Codiff.Models;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Codiff.ViewModels;

public partial class FolderEntryViewModel : ObservableObject
{
    // Light-theme brushes
    private static readonly IBrush ModifiedBrushLight = new SolidColorBrush(Color.Parse("#FFFACD"));
    private static readonly IBrush OnlyInLeftBrushLight = new SolidColorBrush(Color.Parse("#FFCCCC"));
    private static readonly IBrush OnlyInRightBrushLight = new SolidColorBrush(Color.Parse("#CCFFCC"));

    // Dark-theme brushes
    private static readonly IBrush ModifiedBrushDark = new SolidColorBrush(Color.Parse("#4A4200"));
    private static readonly IBrush OnlyInLeftBrushDark = new SolidColorBrush(Color.Parse("#3A0000"));
    private static readonly IBrush OnlyInRightBrushDark = new SolidColorBrush(Color.Parse("#003000"));

    public FolderDiffEntry Model { get; }

    public string RelativePath => Model.RelativePath;
    public string StatusIcon => Model.StatusIcon;
    public string? LeftFullPath => Model.LeftFullPath;
    public string? RightFullPath => Model.RightFullPath;
    public FileStatus Status => Model.Status;

    public IBrush StatusColor => Model.Status switch
    {
        FileStatus.Modified => ThemeService.Instance.IsDark ? ModifiedBrushDark : ModifiedBrushLight,
        FileStatus.OnlyInLeft => ThemeService.Instance.IsDark ? OnlyInLeftBrushDark : OnlyInLeftBrushLight,
        FileStatus.OnlyInRight => ThemeService.Instance.IsDark ? OnlyInRightBrushDark : OnlyInRightBrushLight,
        _ => Brushes.Transparent
    };

    public FolderEntryViewModel(FolderDiffEntry model)
    {
        Model = model;
    }
}
