using System.Collections.ObjectModel;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

public partial class FolderDiffViewModel : ObservableObject
{
    private readonly FolderDiffService _folderService = new();

    // Flat list is kept for potential future use, but tree is primary for UI
    [ObservableProperty]
    private ObservableCollection<FolderEntryViewModel> _entries = [];

    [ObservableProperty]
    private FolderEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private string _leftFolderPath = string.Empty;

    [ObservableProperty]
    private string _rightFolderPath = string.Empty;

    // Root nodes for tree view
    [ObservableProperty]
    private ObservableCollection<FolderNodeViewModel> _rootNodes = [];

    // Currently selected node in tree view
    [ObservableProperty]
    private FolderNodeViewModel? _selectedNode;

    public bool IsVisible { get; set; }

    public event Action<string, string>? FileEntrySelected;

    // Raised when a folder node is activated so the view can synchronize
    // the corresponding folder on the opposite side.
    public event Action<FolderNodeViewModel>? FolderOpened;

    public void LoadFolders(string leftFolder, string rightFolder)
    {
        LeftFolderPath = leftFolder;
        RightFolderPath = rightFolder;

        var result = _folderService.DiffFolders(leftFolder, rightFolder);

        Entries.Clear();
        foreach (var entry in result.Entries)
            Entries.Add(new FolderEntryViewModel(entry));

        BuildTree(result);
    }

    private void BuildTree(Codiff.Models.FolderDiffResult result)
    {
        _rootNodes.Clear();

        // Build a single logical hierarchy keyed by relative path
        var nodeLookup = new Dictionary<string, FolderNodeViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in result.Entries)
        {
            // Only include differing items in the tree
            if (entry.Status == Codiff.Models.FileStatus.Identical)
                continue;

            var parts = entry.RelativePath
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToArray();
            if (parts.Length == 0)
                continue;

            string currentPath = string.Empty;
            FolderNodeViewModel? parent = null;

            for (int i = 0; i < parts.Length; i++)
            {
                currentPath = currentPath.Length == 0
                    ? parts[i]
                    : currentPath + Path.DirectorySeparatorChar + parts[i];

                bool isLeaf = i == parts.Length - 1;
                bool isFile = isLeaf; // treat final segment as file

                if (!nodeLookup.TryGetValue(currentPath, out var node))
                {
                    string? leftPath = null;
                    string? rightPath = null;
                    var status = Codiff.Models.FileStatus.Modified;

                    if (isLeaf)
                    {
                        leftPath = entry.LeftFullPath;
                        rightPath = entry.RightFullPath;
                        status = entry.Status;
                    }

                    node = new FolderNodeViewModel(currentPath, status, leftPath, rightPath, isFile, parent);
                    nodeLookup[currentPath] = node;

                    if (parent == null)
                    {
                        _rootNodes.Add(node);
                    }
                    else
                    {
                        parent.Children.Add(node);
                    }
                }

                parent = node;
            }
        }
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

    // Called from view on double-click
    public void OpenSelectedNode()
    {
        var node = SelectedNode;
        if (node == null)
            return;

        if (!node.IsFile)
        {
            // Notify view that a folder was opened so it can sync the other tree.
            FolderOpened?.Invoke(node);
            return;
        }

        var left = node.LeftFullPath ?? string.Empty;
        var right = node.RightFullPath ?? string.Empty;

        // Only fire when at least one side exists
        if (!string.IsNullOrEmpty(left) || !string.IsNullOrEmpty(right))
        {
            FileEntrySelected?.Invoke(left, right);
        }
    }

    public void OpenFolderInExplorer(bool isLeft)
    {
        var path = isLeft ? LeftFolderPath : RightFolderPath;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", path);
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", path);
            }
        }
        catch
        {
            // Swallow any OS-specific errors; this is a convenience feature.
        }
    }
}
