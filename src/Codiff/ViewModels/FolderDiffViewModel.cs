using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Codiff.Collections;
using Codiff.Models;
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
    /// Expanding or collapsing a single folder inserts/removes only that folder's rows, so the
    /// grids keep their scroll position and selection; bulk changes (load, sort, expand/collapse
    /// all) replace the whole list with a single reset notification.
    /// </summary>
    public BulkObservableCollection<FolderNodeViewModel> FlatRows { get; } = new();

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
    private async Task Recompare()
    {
        if (!string.IsNullOrEmpty(LeftFolderPath) && !string.IsNullOrEmpty(RightFolderPath))
            await LoadFoldersAsync(LeftFolderPath, RightFolderPath);
    }

    private bool CanRecompare() =>
        !string.IsNullOrEmpty(LeftFolderPath) && !string.IsNullOrEmpty(RightFolderPath);

    [RelayCommand(CanExecute = nameof(CanRecompare))]
    private async Task SwapDirectories()
    {
        var left = LeftFolderPath;
        var right = RightFolderPath;
        await LoadFoldersAsync(right, left);
    }

    [RelayCommand(CanExecute = nameof(CanUpOneLevel))]
    private async Task UpOneLevel()
    {
        var leftParent = Directory.GetParent(LeftFolderPath)?.FullName;
        var rightParent = Directory.GetParent(RightFolderPath)?.FullName;
        if (leftParent != null && rightParent != null)
            await LoadFoldersAsync(leftParent, rightParent);
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

        await LoadFoldersAsync(LeftFolderPath, RightFolderPath);
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

        await LoadFoldersAsync(LeftFolderPath, RightFolderPath);
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

    // ── Loading progress ──────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingText = string.Empty;

    [ObservableProperty]
    private double _loadingProgress;

    [ObservableProperty]
    private bool _isProgressIndeterminate = true;

    public async Task LoadFoldersAsync(string leftFolder, string rightFolder)
    {
        if (IsLoading) return;

        SelectedNode = null;
        LeftFolderPath = leftFolder;
        RightFolderPath = rightFolder;

        IsLoading = true;
        IsProgressIndeterminate = true;
        LoadingProgress = 0;
        LoadingText = "Scanning folders…";

        FolderDiffResult result;
        List<FolderNodeViewModel> roots;
        try
        {
            // Progress<T> marshals reports back to the UI thread.
            var progress = new Progress<FolderDiffProgress>(p =>
            {
                if (p.Total > 0)
                {
                    IsProgressIndeterminate = false;
                    LoadingProgress = 100.0 * p.Done / p.Total;
                    LoadingText = $"Comparing files… {p.Done:N0} / {p.Total:N0}";
                }
                else
                {
                    IsProgressIndeterminate = true;
                    LoadingText = p.Phase;
                }
            });

            // Everything up to the finished (but not yet displayed) tree runs off the UI thread.
            (result, roots) = await Task.Run(() =>
            {
                var diff = _folderService.DiffFolders(leftFolder, rightFolder, progress);
                WarmUpIconCache(diff.Entries);
                return (diff, BuildTree(diff));
            });
        }
        finally
        {
            IsLoading = false;
        }

        // Compute summary statistics in a single pass
        int identical = 0, oneSide = 0, modified = 0;
        foreach (var entry in result.Entries)
        {
            switch (entry.Status)
            {
                case FileStatus.Identical: identical++; break;
                case FileStatus.Modified: modified++; break;
                default: oneSide++; break;
            }
        }
        IdenticalCount = identical;
        OneSideCount   = oneSide;
        ModifiedCount  = modified;
        IsStatsVisible = result.Entries.Count > 0;

        ShowTree(roots);
    }

    /// <summary>
    /// Extracts the icons of all items that will be shown in the tree (icons are cached per file
    /// extension), so that creating the tree nodes only hits the cache.
    /// Runs on a background thread.
    /// </summary>
    private static void WarmUpIconCache(IReadOnlyList<FolderDiffEntry> entries)
    {
        var iconService = IconExtractorServiceFactory.Instance;
        iconService.GetFolderIcon(string.Empty);

        // One file per extension is enough (same key normalization as the icon services use).
        var extensions = new HashSet<string>(StringComparer.Ordinal);
        void WarmUp(string? path)
        {
            if (path != null && extensions.Add(Path.GetExtension(path).ToLowerInvariant()))
                iconService.GetFileIcon(path);
        }

        foreach (var entry in entries)
        {
            if (entry.Status == FileStatus.Identical)
                continue;
            WarmUp(entry.LeftFullPath);
            WarmUp(entry.RightFullPath);
        }
    }

    /// <summary>
    /// Creates the tree of differing items and returns its root nodes. The nodes aren't connected to
    /// anything yet, so this runs on a background thread; <see cref="ShowTree"/> then displays them.
    /// </summary>
    private static List<FolderNodeViewModel> BuildTree(FolderDiffResult result)
    {
        var roots = new List<FolderNodeViewModel>();

        // Folder nodes keyed by relative path. Every entry is a file (the scan lists files only),
        // so the last segment of an entry's path is always a file node, with or without an extension.
        var folders = new Dictionary<string, FolderNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        foreach (var entry in result.Entries)
        {
            // Only include differing items in the tree
            if (entry.Status == FileStatus.Identical)
                continue;

            var parts = entry.RelativePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);
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
                if (!isLeaf && folders.TryGetValue(currentPath, out var existingFolder))
                {
                    parent = existingFolder;
                    continue;
                }

                var node = isLeaf
                    ? new FolderNodeViewModel(entry, currentPath, parent)
                    : new FolderNodeViewModel(currentPath, FileStatus.Modified, null, null, isFile: false, parent);
                if (!isLeaf)
                    folders[currentPath] = node;

                if (parent == null)
                    roots.Add(node);
                else
                    parent.AddChild(node);

                parent = node;
            }
        }

        return roots;
    }

    /// <summary>Replaces the displayed tree with <paramref name="roots"/> (UI thread).</summary>
    private void ShowTree(List<FolderNodeViewModel> roots)
    {
        // Unsubscribe from all existing nodes before clearing
        foreach (var node in GetAllNodes(RootNodes))
            node.PropertyChanged -= OnNodePropertyChanged;

        RootNodes.Clear();
        foreach (var root in roots)
            RootNodes.Add(root);

        // Subscribe to new nodes and build the initial flat row list
        foreach (var node in GetAllNodes(RootNodes))
            node.PropertyChanged += OnNodePropertyChanged;

        RebuildFlatRows();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderNodeViewModel.IsExpanded) && !_isBatchUpdating &&
            sender is FolderNodeViewModel node)
        {
            UpdateFlatRowsForToggledNode(node);
        }
    }

    private void RebuildFlatRows()
    {
        var rows = new List<FolderNodeViewModel>(FlatRows.Count);
        foreach (var root in ApplySort(RootNodes))
            AddVisibleRows(root, depth: 0, rows);
        FlatRows.ReplaceAll(rows);
    }

    /// <summary>
    /// Shows or hides the rows below a folder that was expanded or collapsed, using one insert/remove
    /// notification per row. (A reset would scroll both grids back to the top and clear the selection.)
    /// </summary>
    private void UpdateFlatRowsForToggledNode(FolderNodeViewModel node)
    {
        int index = FlatRows.IndexOf(node);
        if (index < 0)
            return; // Inside a collapsed folder: no visible row changes.

        if (node.IsExpanded)
        {
            var rows = new List<FolderNodeViewModel>();
            foreach (var child in ApplySort(node.Children))
                AddVisibleRows(child, node.Depth + 1, rows);

            for (int i = 0; i < rows.Count; i++)
                FlatRows.Insert(index + 1 + i, rows[i]);
        }
        else
        {
            int end = index + 1;
            while (end < FlatRows.Count && FlatRows[end].Depth > node.Depth)
                end++;

            for (int i = end - 1; i > index; i--)
                FlatRows.RemoveAt(i);
        }
    }

    /// <summary>Appends <paramref name="node"/> and its visible descendants, in display order.</summary>
    private void AddVisibleRows(FolderNodeViewModel node, int depth, List<FolderNodeViewModel> rows)
    {
        node.Depth = depth;
        rows.Add(node);

        if (!node.IsFile && node.IsExpanded)
        {
            foreach (var child in ApplySort(node.Children))
                AddVisibleRows(child, depth + 1, rows);
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

    /// <summary>All nodes of the tree in pre-order (iterative, so deep trees don't nest iterators).</summary>
    private static IEnumerable<FolderNodeViewModel> GetAllNodes(IReadOnlyList<FolderNodeViewModel> roots)
    {
        var stack = new Stack<FolderNodeViewModel>();
        for (int i = roots.Count - 1; i >= 0; i--)
            stack.Push(roots[i]);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
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

        FileManagerService.OpenFolder(path);
    }
}
