using System.Collections.ObjectModel;
using Codiff.Models;
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
    public ObservableCollection<RecentComparisonViewModel> RecentComparisons { get; } = [];

    /// <summary>True when there is at least one recent comparison entry.</summary>
    public bool HasRecentComparisons => RecentComparisons.Count > 0;

    public Func<Task<(string? left, string? right)>>? OpenFilesDialog;
    public Func<Task<(string? left, string? right)>>? OpenFoldersDialog;
    public Action? ExitApplication;

    public MainWindowViewModel()
    {
        FolderDiffViewModel.FileEntrySelected += OnFileEntrySelected;
        FolderDiffViewModel.PropertyChanged   += OnFolderDiffViewModelPropertyChanged;
    }

    /// <summary>
    /// Populates <see cref="RecentComparisons"/> from the persisted config.
    /// Call once after <see cref="ConfigService.Instance"/> has been loaded.
    /// </summary>
    public void LoadFromConfig()
    {
        RecentComparisons.Clear();
        foreach (var entry in ConfigService.Instance.Config.RecentComparisons)
            RecentComparisons.Add(new RecentComparisonViewModel(entry, OpenRecentComparison));
        OnPropertyChanged(nameof(HasRecentComparisons));
    }

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
        {
            IsFolderViewVisible = false;
            FileDiffViewModel.LoadFiles(left, right);
            AddRecentComparison(isFolder: false, left, right);
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
            FolderDiffViewModel.IsVisible = true;
            AddRecentComparison(isFolder: true, left, right);
            await FolderDiffViewModel.LoadFoldersAsync(left, right);
        }
    }

    [RelayCommand]
    private void ClearRecent()
    {
        ConfigService.Instance.ClearRecentComparisons();
        RecentComparisons.Clear();
        OnPropertyChanged(nameof(HasRecentComparisons));
    }

    private void OpenRecentComparison(RecentComparisonViewModel entry)
    {
        if (entry.IsFolder)
        {
            IsFolderViewVisible = true;
            FolderDiffViewModel.IsVisible = true;
            _ = FolderDiffViewModel.LoadFoldersAsync(entry.LeftPath, entry.RightPath);
        }
        else
        {
            IsFolderViewVisible = false;
            FileDiffViewModel.LoadFiles(entry.LeftPath, entry.RightPath);
        }
        // Promote to top of recent list
        AddRecentComparison(entry.IsFolder, entry.LeftPath, entry.RightPath);
    }

    private void AddRecentComparison(bool isFolder, string leftPath, string rightPath)
    {
        ConfigService.Instance.AddRecentComparison(isFolder, leftPath, rightPath);

        // Rebuild the observable collection to match the new order
        RecentComparisons.Clear();
        foreach (var entry in ConfigService.Instance.Config.RecentComparisons)
            RecentComparisons.Add(new RecentComparisonViewModel(entry, OpenRecentComparison));
        OnPropertyChanged(nameof(HasRecentComparisons));
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

    /// <summary>
    /// When a file node is single-clicked in the folder view, load its content
    /// into <see cref="FileDiffViewModel"/> so that "Copy as Text" in the main
    /// menu is enabled without needing to open the full diff window.
    /// </summary>
    private void OnFolderDiffViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderDiffViewModel.SelectedNode)) return;

        var node = FolderDiffViewModel.SelectedNode;
        if (node == null || !node.IsFile) return;

        var left  = node.LeftFullPath  ?? string.Empty;
        var right = node.RightFullPath ?? string.Empty;

        try
        {
            if (!string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right))
                FileDiffViewModel.LoadFiles(left, right);
            else if (!string.IsNullOrEmpty(left))
                FileDiffViewModel.LoadTexts(File.ReadAllText(left), string.Empty, left, string.Empty);
            else if (!string.IsNullOrEmpty(right))
                FileDiffViewModel.LoadTexts(string.Empty, File.ReadAllText(right), string.Empty, right);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindowViewModel] Failed to preview selected file: {ex.Message}");
        }
    }

    partial void OnIsFolderViewVisibleChanged(bool value)
    {
        FolderDiffViewModel.IsVisible = value;
    }
}
