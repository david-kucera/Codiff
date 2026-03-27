using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private FileDiffViewModel _fileDiffViewModel = new();

    [ObservableProperty]
    private FolderDiffViewModel _folderDiffViewModel = new();

    [ObservableProperty]
    private bool _isFolderViewVisible;

    public Func<Task<(string? left, string? right)>>? OpenFilesDialog;
    public Func<Task<(string? left, string? right)>>? OpenFoldersDialog;

    public MainWindowViewModel()
    {
        FolderDiffViewModel.FileEntrySelected += OnFileEntrySelected;
    }

    [RelayCommand]
    private async Task OpenFiles()
    {
        if (OpenFilesDialog == null) return;
        var (left, right) = await OpenFilesDialog();
        if (left != null && right != null)
        {
            IsFolderViewVisible = false;
            FileDiffViewModel.LoadFiles(left, right);
        }
    }

    [RelayCommand]
    private async Task OpenFolders()
    {
        if (OpenFoldersDialog == null) return;
        var (left, right) = await OpenFoldersDialog();
        if (left != null && right != null)
        {
            IsFolderViewVisible = true;
            FolderDiffViewModel.LoadFolders(left, right);
        }
    }

    private void OnFileEntrySelected(string leftPath, string rightPath)
    {
        if (!string.IsNullOrEmpty(leftPath) && !string.IsNullOrEmpty(rightPath))
        {
            FileDiffViewModel.LoadFiles(leftPath, rightPath);
        }
        else if (!string.IsNullOrEmpty(leftPath))
        {
            FileDiffViewModel.LoadTexts(File.ReadAllText(leftPath), string.Empty, leftPath, string.Empty);
        }
        else if (!string.IsNullOrEmpty(rightPath))
        {
            FileDiffViewModel.LoadTexts(string.Empty, File.ReadAllText(rightPath), string.Empty, rightPath);
        }
    }
}
