using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Codiff.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Codiff.ViewModels;

/// <summary>Column by which the folder-diff tree is currently sorted.</summary>
public enum SortColumn { None, Name, Size, Type, Time }

/// <summary>Controls which directory panels are visible in the folder diff view.</summary>
public enum ViewMode { Both, OnlyLeft, OnlyRight }

public partial class FolderDiffViewModel : ObservableObject
{
    private readonly FolderDiffService _folderService = new();

    // Flat list is kept for potential future use, but tree is primary for UI
    [ObservableProperty]
    private ObservableCollection<FolderEntryViewModel> _entries = [];

    [ObservableProperty]
    private FolderEntryViewModel? _selectedEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecompareCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwapDirectoriesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpOneLevelCommand))]
    private string _leftFolderPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecompareCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwapDirectoriesCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpOneLevelCommand))]
    private string _rightFolderPath = string.Empty;

    // Root nodes for tree view
    [ObservableProperty]
    private ObservableCollection<FolderNodeViewModel> _rootNodes = [];

    // Currently selected node in tree view
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyToClipboardCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteFromBothCommand))]
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

    // ── View mode ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private ViewMode _currentViewMode = ViewMode.Both;

    [RelayCommand]
    private void ShowBothDirectories() => CurrentViewMode = ViewMode.Both;

    [RelayCommand]
    private void ShowOnlyFirstDirectory() => CurrentViewMode = ViewMode.OnlyLeft;

    [RelayCommand]
    private void ShowOnlySecondDirectory() => CurrentViewMode = ViewMode.OnlyRight;

    // ── Expand / Collapse all ──────────────────────────────────────────────

    private bool _isBatchUpdating;

    [RelayCommand]
    private void ExpandAll()
    {
        _isBatchUpdating = true;
        try
        {
            foreach (var node in GetAllNodes(RootNodes))
            {
                if (node.IsExpandable)
                    node.IsExpanded = true;
            }
        }
        finally
        {
            _isBatchUpdating = false;
            RebuildFlatRows();
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        _isBatchUpdating = true;
        try
        {
            foreach (var node in GetAllNodes(RootNodes))
            {
                if (node.IsExpandable)
                    node.IsExpanded = false;
            }
        }
        finally
        {
            _isBatchUpdating = false;
            RebuildFlatRows();
        }
    }

    // ── Selection side ────────────────────────────────────────────────────

    /// <summary>The side (left or right DataGrid) that was most recently interacted with.</summary>
    public FolderSide SelectedSide { get; set; } = FolderSide.Left;

    // ── Directory operations ───────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRecompare))]
    private void Recompare()
    {
        if (!string.IsNullOrEmpty(LeftFolderPath) && !string.IsNullOrEmpty(RightFolderPath))
            LoadFolders(LeftFolderPath, RightFolderPath);
    }

    private bool CanRecompare() =>
        !string.IsNullOrEmpty(LeftFolderPath) && !string.IsNullOrEmpty(RightFolderPath);

    [RelayCommand(CanExecute = nameof(CanRecompare))]
    private void SwapDirectories()
    {
        var left = LeftFolderPath;
        var right = RightFolderPath;
        LoadFolders(right, left);
    }

    [RelayCommand(CanExecute = nameof(CanUpOneLevel))]
    private void UpOneLevel()
    {
        var leftParent = Directory.GetParent(LeftFolderPath)?.FullName;
        var rightParent = Directory.GetParent(RightFolderPath)?.FullName;
        if (leftParent != null && rightParent != null)
            LoadFolders(leftParent, rightParent);
    }

    private bool CanUpOneLevel()
    {
        if (string.IsNullOrEmpty(LeftFolderPath) || string.IsNullOrEmpty(RightFolderPath))
            return false;
        return Directory.GetParent(LeftFolderPath) != null &&
               Directory.GetParent(RightFolderPath) != null;
    }

    // ── Clipboard ─────────────────────────────────────────────────────────

    /// <summary>Fired when a path should be copied to the clipboard. The view handles the actual write.</summary>
    public event Action<string>? CopyToClipboardRequested;

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void CopyToClipboard()
    {
        var node = SelectedNode;
        if (node == null) return;

        var path = SelectedSide == FolderSide.Left
            ? (node.LeftFullPath ?? node.RightFullPath)
            : (node.RightFullPath ?? node.LeftFullPath);

        if (!string.IsNullOrEmpty(path))
            CopyToClipboardRequested?.Invoke(path);
    }

    private bool CanActOnSelection() => SelectedNode != null;

    // ── Delete ─────────────────────────────────────────────────────────────

    /// <summary>Set by the view to show a confirmation dialog. Returns true when the user confirms.</summary>
    public Func<string, Task<bool>>? ConfirmDeleteAsync { get; set; }

    /// <summary>Fired when a delete operation fails. The view can show an error message.</summary>
    public event Action<string>? DeleteErrorOccurred;

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task Delete()
    {
        var node = SelectedNode;
        if (node == null) return;

        var path = SelectedSide == FolderSide.Left ? node.LeftFullPath : node.RightFullPath;
        if (string.IsNullOrEmpty(path)) return;

        // Validate the path is within the known folder boundaries
        var expectedFolder = SelectedSide == FolderSide.Left ? LeftFolderPath : RightFolderPath;
        if (!IsPathWithinFolder(path, expectedFolder))
        {
            System.Diagnostics.Debug.WriteLine($"[FolderDiffViewModel] Delete rejected – path '{path}' is outside expected folder '{expectedFolder}'.");
            return;
        }

        if (ConfirmDeleteAsync != null && !await ConfirmDeleteAsync($"Delete '{path}'?"))
            return;

        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderDiffViewModel] Delete failed for '{path}': {ex}");
            DeleteErrorOccurred?.Invoke($"Could not delete '{Path.GetFileName(path)}':\n{ex.Message}");
            return;
        }

        LoadFolders(LeftFolderPath, RightFolderPath);
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task DeleteFromBoth()
    {
        var node = SelectedNode;
        if (node == null) return;

        // Validate and collect only safe paths
        var paths = new[]
            {
                (path: node.LeftFullPath,  folder: LeftFolderPath),
                (path: node.RightFullPath, folder: RightFolderPath)
            }
            .Where(t => !string.IsNullOrEmpty(t.path) && IsPathWithinFolder(t.path!, t.folder))
            .Select(t => t.path!)
            .ToList();

        if (paths.Count == 0) return;

        var message = paths.Count == 1
            ? $"Delete '{paths[0]}'?"
            : $"Delete '{paths[0]}'\nand '{paths[1]}'?";

        if (ConfirmDeleteAsync != null && !await ConfirmDeleteAsync(message))
            return;

        var errors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FolderDiffViewModel] DeleteFromBoth failed for '{path}': {ex}");
                errors.Add($"'{Path.GetFileName(path)}': {ex.Message}");
            }
        }

        if (errors.Count > 0)
            DeleteErrorOccurred?.Invoke("Some items could not be deleted:\n" + string.Join("\n", errors));

        LoadFolders(LeftFolderPath, RightFolderPath);
    }

    /// <summary>
    /// Returns true only when <paramref name="filePath"/> resides inside
    /// <paramref name="folderPath"/>, preventing accidental out-of-tree deletions.
    /// </summary>
    private static bool IsPathWithinFolder(string filePath, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(folderPath))
            return false;
        try
        {
            var fullFile   = Path.GetFullPath(filePath);
            var fullFolder = Path.GetFullPath(folderPath);
            var relative   = Path.GetRelativePath(fullFolder, fullFile);
            if (Path.IsPathRooted(relative))
                return false;
            return !relative.Equals("..") &&
                   !relative.StartsWith(".." + Path.DirectorySeparatorChar) &&
                   !relative.StartsWith(".." + Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────

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
        SelectedNode = null;
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
        if (e.PropertyName == nameof(FolderNodeViewModel.IsExpanded) && !_isBatchUpdating)
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
