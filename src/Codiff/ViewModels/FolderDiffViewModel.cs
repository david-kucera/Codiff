using System.Collections.ObjectModel;
using Codiff.Models;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

public partial class FolderDiffViewModel : ObservableObject
{
    private readonly FolderDiffService _folderService = new();

    [ObservableProperty]
    private ObservableCollection<FolderEntryViewModel> _entries = new();

    [ObservableProperty]
    private FolderEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private string _leftFolderPath = string.Empty;

    [ObservableProperty]
    private string _rightFolderPath = string.Empty;

    public event Action<string, string>? FileEntrySelected;

    public void LoadFolders(string leftFolder, string rightFolder)
    {
        LeftFolderPath = leftFolder;
        RightFolderPath = rightFolder;

        var result = _folderService.DiffFolders(leftFolder, rightFolder);
        Entries.Clear();
        foreach (var entry in result.Entries)
            Entries.Add(new FolderEntryViewModel(entry));
    }

    [RelayCommand]
    private void SelectEntry(FolderEntryViewModel? entry)
    {
        if (entry == null) return;
        SelectedEntry = entry;

        if (entry.LeftFullPath != null && entry.RightFullPath != null)
        {
            FileEntrySelected?.Invoke(entry.LeftFullPath, entry.RightFullPath);
        }
        else if (entry.LeftFullPath != null)
        {
            FileEntrySelected?.Invoke(entry.LeftFullPath, string.Empty);
        }
        else if (entry.RightFullPath != null)
        {
            FileEntrySelected?.Invoke(string.Empty, entry.RightFullPath);
        }
    }
}
