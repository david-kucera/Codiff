using Codiff.Collections;
using Codiff.Services;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeMenuHeader))]
    private bool _isDarkTheme;

    public string ThemeMenuHeader => IsDarkTheme ? "Switch to Light Theme" : "Switch to Dark Theme";

    /// <summary>Recent comparisons shown in the "Recent Files/Directories" menu.</summary>
    public BulkObservableCollection<RecentComparisonViewModel> RecentComparisons { get; } = new();

    /// <summary>True when there is at least one recent comparison entry.</summary>
    public bool HasRecentComparisons => RecentComparisons.Count > 0;

    public Func<Task<(string? left, string? right)>>? OpenFilesDialog;
    public Func<Task<(string? left, string? right)>>? OpenFoldersDialog;
    public Action? ExitApplication;

    /// <summary>Set by the view to show an error message to the user.</summary>
    public Func<string, Task>? ShowErrorAsync;

    public MainWindowViewModel()
    {
        FolderDiffViewModel.FileEntrySelected += OnFileEntrySelected;
        FolderDiffViewModel.PropertyChanged   += OnFolderDiffViewModelPropertyChanged;
    }

    /// <summary>
    /// Populates <see cref="RecentComparisons"/> from the persisted config.
    /// Call once after <see cref="ConfigService.Instance"/> has been loaded.
    /// </summary>
    public void LoadFromConfig() => RefreshRecentComparisons();

    [RelayCommand]
    private void Exit() => ExitApplication?.Invoke();

    [RelayCommand]
    private void ToggleTheme()
    {
        ThemeService.Instance.Toggle();
        IsDarkTheme = ThemeService.Instance.IsDark;
    }

    [RelayCommand]
    private async Task OpenFiles()
    {
        if (OpenFilesDialog == null) return;
        var (left, right) = await OpenFilesDialog();
        if (left != null && right != null)
            await OpenFilesAsync(left, right);
    }

    [RelayCommand]
    private async Task OpenFolders()
    {
        if (OpenFoldersDialog == null) return;
        var (left, right) = await OpenFoldersDialog();
        if (left != null && right != null)
            await OpenFoldersAsync(left, right);
    }

    /// <summary>
    /// Compares two files or two folders, whichever the paths point to
    /// (command-line arguments, drag and drop).
    /// </summary>
    public async Task OpenComparisonAsync(string leftPath, string rightPath)
    {
        leftPath = Path.GetFullPath(leftPath);
        rightPath = Path.GetFullPath(rightPath);

        if (Directory.Exists(leftPath) && Directory.Exists(rightPath))
            await OpenFoldersAsync(leftPath, rightPath);
        else if (File.Exists(leftPath) && File.Exists(rightPath))
            await OpenFilesAsync(leftPath, rightPath);
        else
            await ShowError($"Cannot compare\n{leftPath}\nwith\n{rightPath}\n\nBoth must be existing files or both existing folders.");
    }

    /// <summary>Replaces one side of the shown comparison (a file or folder dropped onto that side).</summary>
    public async Task ReplaceSideAsync(string path, bool isLeft)
    {
        string other = IsFolderViewVisible
            ? (isLeft ? FolderDiffViewModel.RightFolderPath : FolderDiffViewModel.LeftFolderPath)
            : (isLeft ? FileDiffViewModel.RightFilePath : FileDiffViewModel.LeftFilePath);
        if (string.IsNullOrEmpty(other))
            return;

        await OpenComparisonAsync(isLeft ? path : other, isLeft ? other : path);
    }

    private async Task OpenFilesAsync(string left, string right)
    {
        try
        {
            FileDiffViewModel.LoadFiles(left, right);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await ShowError($"Could not open the files:\n{ex.Message}");
            return;
        }
        IsFolderViewVisible = false;
        AddRecentComparison(isFolder: false, left, right);
    }

    private async Task OpenFoldersAsync(string left, string right)
    {
        var missing = new[] { left, right }.FirstOrDefault(path => !Directory.Exists(path));
        if (missing != null)
        {
            await ShowError($"The folder does not exist:\n{missing}");
            return;
        }

        IsFolderViewVisible = true;
        AddRecentComparison(isFolder: true, left, right);
        try
        {
            await FolderDiffViewModel.LoadFoldersAsync(left, right);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowError($"Could not compare the folders:\n{ex.Message}");
        }
    }

    private Task ShowError(string message) => ShowErrorAsync?.Invoke(message) ?? Task.CompletedTask;

    [RelayCommand]
    private void ClearRecent()
    {
        ConfigService.Instance.ClearRecentComparisons();
        RecentComparisons.Clear();
        OnPropertyChanged(nameof(HasRecentComparisons));
    }

    private void OpenRecentComparison(RecentComparisonViewModel entry)
    {
        // Opening also promotes the entry to the top of the recent list.
        _ = entry.IsFolder
            ? OpenFoldersAsync(entry.LeftPath, entry.RightPath)
            : OpenFilesAsync(entry.LeftPath, entry.RightPath);
    }

    private void AddRecentComparison(bool isFolder, string leftPath, string rightPath)
    {
        ConfigService.Instance.AddRecentComparison(isFolder, leftPath, rightPath);
        RefreshRecentComparisons();
    }

    /// <summary>Rebuilds <see cref="RecentComparisons"/> from the config (one change notification).</summary>
    private void RefreshRecentComparisons()
    {
        RecentComparisons.ReplaceAll(ConfigService.Instance.Config.RecentComparisons
            .Select(entry => new RecentComparisonViewModel(entry, OpenRecentComparison)));
        OnPropertyChanged(nameof(HasRecentComparisons));
    }

    private void OnFileEntrySelected(string leftPath, string rightPath) => PreviewFiles(leftPath, rightPath);

    /// <summary>
    /// When a file node is single-clicked in the folder view, select its files in
    /// <see cref="FileDiffViewModel"/> so that "Copy as Text" in the main menu is enabled
    /// without needing to open the full diff window.
    /// </summary>
    private void OnFolderDiffViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderDiffViewModel.SelectedNode)) return;

        var node = FolderDiffViewModel.SelectedNode;
        if (node == null || !node.IsFile) return;

        PreviewFiles(node.LeftFullPath ?? string.Empty, node.RightFullPath ?? string.Empty);
    }

    /// <summary>
    /// Selects the files without comparing them: the (hidden) file view only needs them for
    /// "Copy as Text", so the diff is computed on demand rather than on every click in the tree.
    /// </summary>
    private void PreviewFiles(string leftPath, string rightPath)
    {
        if (string.IsNullOrEmpty(leftPath) && string.IsNullOrEmpty(rightPath)) return;
        FileDiffViewModel.SetPendingFiles(leftPath, rightPath);
    }

    partial void OnIsFolderViewVisibleChanged(bool value)
    {
        // The file view is being shown: compare a pending preview selection, if any.
        if (!value)
            FileDiffViewModel.EnsureLoaded();
    }
}
