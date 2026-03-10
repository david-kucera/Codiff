using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Codiff.ViewModels;

namespace Codiff.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenFilesDialog = OpenFilesDialogAsync;
            vm.OpenFoldersDialog = OpenFoldersDialogAsync;
        }
    }

    private async Task<(string? left, string? right)> OpenFilesDialogAsync()
    {
        var leftFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Left File",
            AllowMultiple = false
        });
        if (leftFiles.Count == 0) return (null, null);

        var rightFiles = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Right File",
            AllowMultiple = false
        });
        if (rightFiles.Count == 0) return (null, null);

        return (leftFiles[0].Path.LocalPath, rightFiles[0].Path.LocalPath);
    }

    private async Task<(string? left, string? right)> OpenFoldersDialogAsync()
    {
        var leftFolders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Left Folder",
            AllowMultiple = false
        });
        if (leftFolders.Count == 0) return (null, null);

        var rightFolders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Right Folder",
            AllowMultiple = false
        });
        if (rightFolders.Count == 0) return (null, null);

        return (leftFolders[0].Path.LocalPath, rightFolders[0].Path.LocalPath);
    }
}
