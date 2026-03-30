using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

/// <summary>Column by which the folder-diff tree is currently sorted.</summary>
public enum SortColumn { None, Name, Size, Type, Time }

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

    /// <summary>
    /// Flat, ordered list of currently visible tree nodes.
    /// Expand/collapse actions rebuild this list in-place.
    /// </summary>
    public ObservableCollection<FolderNodeViewModel> FlatRows { get; } = [];

    // ── Sort state ────────────────────────────────────────────────────────

    /// <summary>Sort-direction indicator appended to the active column's header when ascending.</summary>
    public const string AscendingIndicator  = " ↑";

    /// <summary>Sort-direction indicator appended to the active column's header when descending.</summary>
    public const string DescendingIndicator = " ↓";

    private SortColumn _currentSortColumn = SortColumn.None;
    private bool _sortAscending = true;

    /// <summary>
    /// Header text for the Name column, including the sort-direction arrow when active.
    /// </summary>
    public string NameSortHeader => BuildSortHeader("Name", SortColumn.Name);

    /// <summary>
    /// Header text for the Size column, including the sort-direction arrow when active.
    /// </summary>
    public string SizeSortHeader => BuildSortHeader("Size", SortColumn.Size);

    /// <summary>
    /// Header text for the Type column, including the sort-direction arrow when active.
    /// </summary>
    public string TypeSortHeader => BuildSortHeader("Type", SortColumn.Type);

    /// <summary>
    /// Header text for the Time column, including the sort-direction arrow when active.
    /// </summary>
    public string TimeSortHeader => BuildSortHeader("Time", SortColumn.Time);

    private string BuildSortHeader(string name, SortColumn col)
    {
        if (_currentSortColumn != col) return name;
        return _sortAscending ? name + AscendingIndicator : name + DescendingIndicator;
    }

    [RelayCommand]
    private void SortByColumn(string? columnName)
    {
        if (string.IsNullOrEmpty(columnName)) return;

        var col = columnName switch
        {
            "Name" => SortColumn.Name,
            "Size" => SortColumn.Size,
            "Type" => SortColumn.Type,
            "Time" => SortColumn.Time,
            _      => SortColumn.None
        };

        if (col == SortColumn.None) return;

        if (_currentSortColumn == col)
        {
            // Cycle: Asc → Desc → None
            if (_sortAscending)
            {
                _sortAscending = false;
            }
            else
            {
                _currentSortColumn = SortColumn.None;
                _sortAscending = true;
            }
        }
        else
        {
            _currentSortColumn = col;
            _sortAscending = true;
        }

        OnPropertyChanged(nameof(NameSortHeader));
        OnPropertyChanged(nameof(SizeSortHeader));
        OnPropertyChanged(nameof(TypeSortHeader));
        OnPropertyChanged(nameof(TimeSortHeader));

        RebuildFlatRows();
    }

    public bool IsVisible { get; set; }

    // ── Statistics ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCount))]
    [NotifyPropertyChangedFor(nameof(IdenticalPercent))]
    [NotifyPropertyChangedFor(nameof(OneSidePercent))]
    [NotifyPropertyChangedFor(nameof(ModifiedPercent))]
    [NotifyPropertyChangedFor(nameof(IdenticalLabel))]
    [NotifyPropertyChangedFor(nameof(OneSideLabel))]
    [NotifyPropertyChangedFor(nameof(ModifiedLabel))]
    [NotifyPropertyChangedFor(nameof(IdenticalBarStar))]
    [NotifyPropertyChangedFor(nameof(OneSideBarStar))]
    [NotifyPropertyChangedFor(nameof(ModifiedBarStar))]
    private int _identicalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCount))]
    [NotifyPropertyChangedFor(nameof(IdenticalPercent))]
    [NotifyPropertyChangedFor(nameof(OneSidePercent))]
    [NotifyPropertyChangedFor(nameof(ModifiedPercent))]
    [NotifyPropertyChangedFor(nameof(IdenticalLabel))]
    [NotifyPropertyChangedFor(nameof(OneSideLabel))]
    [NotifyPropertyChangedFor(nameof(ModifiedLabel))]
    [NotifyPropertyChangedFor(nameof(IdenticalBarStar))]
    [NotifyPropertyChangedFor(nameof(OneSideBarStar))]
    [NotifyPropertyChangedFor(nameof(ModifiedBarStar))]
    private int _oneSideCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCount))]
    [NotifyPropertyChangedFor(nameof(IdenticalPercent))]
    [NotifyPropertyChangedFor(nameof(OneSidePercent))]
    [NotifyPropertyChangedFor(nameof(ModifiedPercent))]
    [NotifyPropertyChangedFor(nameof(IdenticalLabel))]
    [NotifyPropertyChangedFor(nameof(OneSideLabel))]
    [NotifyPropertyChangedFor(nameof(ModifiedLabel))]
    [NotifyPropertyChangedFor(nameof(IdenticalBarStar))]
    [NotifyPropertyChangedFor(nameof(OneSideBarStar))]
    [NotifyPropertyChangedFor(nameof(ModifiedBarStar))]
    private int _modifiedCount;

    [ObservableProperty]
    private bool _isStatsVisible;

    public int TotalCount => IdenticalCount + OneSideCount + ModifiedCount;

    public string IdenticalPercent => TotalCount > 0 ? $"{100.0 * IdenticalCount / TotalCount:0.0}%" : "0.0%";
    public string OneSidePercent  => TotalCount > 0 ? $"{100.0 * OneSideCount  / TotalCount:0.0}%" : "0.0%";
    public string ModifiedPercent => TotalCount > 0 ? $"{100.0 * ModifiedCount / TotalCount:0.0}%" : "0.0%";

    public string IdenticalLabel => $"Identical: {IdenticalCount} ({IdenticalPercent})";
    public string OneSideLabel   => $"One side only: {OneSideCount} ({OneSidePercent})";
    public string ModifiedLabel  => $"Modified: {ModifiedCount} ({ModifiedPercent})";

    /// <summary>Star-width for the identical (black) segment of the stacked bar.</summary>
    public GridLength IdenticalBarStar => new GridLength(IdenticalCount, GridUnitType.Star);
    /// <summary>Star-width for the one-side-only (green) segment.</summary>
    public GridLength OneSideBarStar   => new GridLength(OneSideCount,   GridUnitType.Star);
    /// <summary>Star-width for the modified (yellow) segment.</summary>
    public GridLength ModifiedBarStar  => new GridLength(ModifiedCount,  GridUnitType.Star);

    [RelayCommand]
    private void CloseStats() => IsStatsVisible = false;

    [RelayCommand]
    private void ShowStats() => IsStatsVisible = true;

    // ─────────────────────────────────────────────────────────────────────

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

        // Compute summary statistics
        IdenticalCount = result.Entries.Count(e => e.Status == Codiff.Models.FileStatus.Identical);
        OneSideCount   = result.Entries.Count(e => e.Status == Codiff.Models.FileStatus.OnlyInLeft
                                                || e.Status == Codiff.Models.FileStatus.OnlyInRight);
        ModifiedCount  = result.Entries.Count(e => e.Status == Codiff.Models.FileStatus.Modified);
        IsStatsVisible = result.Entries.Count > 0;

        BuildTree(result);
    }

    private void BuildTree(Codiff.Models.FolderDiffResult result)
    {
        // Unsubscribe from all existing nodes before clearing
        foreach (var node in GetAllNodes(RootNodes))
            node.PropertyChanged -= OnNodePropertyChanged;

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
                bool isFile = isLeaf && !string.IsNullOrEmpty(Path.GetExtension(parts[i]));

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

        // Subscribe to new nodes and build the initial flat row list
        foreach (var node in GetAllNodes(RootNodes))
            node.PropertyChanged += OnNodePropertyChanged;

        RebuildFlatRows();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderNodeViewModel.IsExpanded))
            RebuildFlatRows();
    }

    private void RebuildFlatRows()
    {
        FlatRows.Clear();
        foreach (var root in ApplySort(RootNodes))
            AddNodeToFlatRows(root, depth: 0);
    }

    private void AddNodeToFlatRows(FolderNodeViewModel node, int depth)
    {
        node.Depth = depth;
        FlatRows.Add(node);

        if (!node.IsFile && node.IsExpanded)
        {
            foreach (var child in ApplySort(node.Children))
                AddNodeToFlatRows(child, depth + 1);
        }
    }

    /// <summary>
    /// Returns <paramref name="nodes"/> in sorted order according to the current
    /// sort column and direction, or in their original order when no sort is active.
    /// </summary>
    private IEnumerable<FolderNodeViewModel> ApplySort(IEnumerable<FolderNodeViewModel> nodes)
    {
        if (_currentSortColumn == SortColumn.None)
            return nodes;

        // Pick the sort key for the node; prefer the left side, fall back to right.
        IOrderedEnumerable<FolderNodeViewModel> ordered = _currentSortColumn switch
        {
            SortColumn.Name => _sortAscending
                ? nodes.OrderBy(n => n.LeftName, StringComparer.OrdinalIgnoreCase)
                : nodes.OrderByDescending(n => n.LeftName, StringComparer.OrdinalIgnoreCase),

            SortColumn.Size => _sortAscending
                ? nodes.OrderBy(n => n.LeftSizeBytes ?? n.RightSizeBytes)
                : nodes.OrderByDescending(n => n.LeftSizeBytes ?? n.RightSizeBytes),

            SortColumn.Type => _sortAscending
                ? nodes.OrderBy(
                    n => string.IsNullOrEmpty(n.LeftType) ? n.RightType : n.LeftType,
                    StringComparer.OrdinalIgnoreCase)
                : nodes.OrderByDescending(
                    n => string.IsNullOrEmpty(n.LeftType) ? n.RightType : n.LeftType,
                    StringComparer.OrdinalIgnoreCase),

            SortColumn.Time => _sortAscending
                ? nodes.OrderBy(n => n.LeftTimeRaw ?? n.RightTimeRaw)
                : nodes.OrderByDescending(n => n.LeftTimeRaw ?? n.RightTimeRaw),

            _ => nodes.OrderBy(_ => 0)
        };

        return ordered;
    }

    private static IEnumerable<FolderNodeViewModel> GetAllNodes(IEnumerable<FolderNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var descendant in GetAllNodes(node.Children))
                yield return descendant;
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
