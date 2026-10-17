using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Codiff.Services;
using Codiff.ViewModels;

namespace Codiff.Views;

public partial class FolderDiffView : UserControl
{
    // DataGrid.ProcessVerticalScroll(ScrollEventType) is the private method that
    // performs the full scroll pipeline: ScrollSlotIntoView + InvalidateRowsMeasure.
    // It reads the target scrollbar's Value, so we must set that first.
    // (DataGrid.SetVerticalOffset only stores _verticalOffset and updates the
    //  scrollbar thumb — it does NOT move the virtualised rows.)
    private static readonly MethodInfo? s_processVerticalScroll =
        typeof(DataGrid).GetMethod("ProcessVerticalScroll",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(ScrollEventType) }, null);

    private bool _isSyncing;
    private bool _gridsSetUp;
    private FolderDiffViewModel? _subscribedViewModel;

    public FolderDiffView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        if (s_processVerticalScroll == null)
            System.Diagnostics.Debug.WriteLine(
                "[FolderDiffView] WARNING: DataGrid.ProcessVerticalScroll not found. " +
                "Vertical scroll synchronisation will be disabled. " +
                "Check if the Avalonia.Controls.DataGrid package was updated.");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetupGrids();
        ThemeService.Instance.ThemeChanged += OnThemeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        if (_subscribedViewModel == null) return;
        foreach (var node in _subscribedViewModel.FlatRows)
            node.NotifyThemeChanged();
    }

    // ── Path-label taps ────────────────────────────────────────────────────

    private void OnLeftPathDoubleTapped(object? sender, RoutedEventArgs e)
        => (DataContext as FolderDiffViewModel)?.OpenFolderInExplorer(isLeft: true);

    private void OnRightPathDoubleTapped(object? sender, RoutedEventArgs e)
        => (DataContext as FolderDiffViewModel)?.OpenFolderInExplorer(isLeft: false);

    // ── DataContext lifecycle ───────────────────────────────────────────────

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.FolderOpened -= OnFolderOpened;
            _subscribedViewModel.FileEntrySelected -= OnFileEntrySelected;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel.CopyToClipboardRequested -= OnCopyToClipboardRequested;
            _subscribedViewModel.DeleteErrorOccurred -= OnDeleteErrorOccurred;
            _subscribedViewModel.ConfirmDeleteAsync = null;
        }

        _subscribedViewModel = DataContext as FolderDiffViewModel;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.FolderOpened += OnFolderOpened;
            _subscribedViewModel.FileEntrySelected += OnFileEntrySelected;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedViewModel.CopyToClipboardRequested += OnCopyToClipboardRequested;
            _subscribedViewModel.DeleteErrorOccurred += OnDeleteErrorOccurred;
            _subscribedViewModel.ConfirmDeleteAsync = ShowConfirmDialogAsync;
        }

        UpdateStatsRowHeight();
        UpdateViewMode();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderDiffViewModel.IsStatsVisible))
            UpdateStatsRowHeight();

        if (e.PropertyName is nameof(FolderDiffViewModel.NameSortHeader)
                           or nameof(FolderDiffViewModel.SizeSortHeader)
                           or nameof(FolderDiffViewModel.TypeSortHeader)
                           or nameof(FolderDiffViewModel.TimeSortHeader))
        {
            if (_subscribedViewModel != null)
                ApplySortHeaders(_subscribedViewModel);
        }

        if (e.PropertyName == nameof(FolderDiffViewModel.CurrentViewMode))
            UpdateViewMode();
    }

    /// <summary>
    /// Collapses or restores Row 1 (splitter) and Row 2 (stats panel) of
    /// MainContentGrid based on the view-model's IsStatsVisible flag.
    /// </summary>
    private void UpdateStatsRowHeight()
    {
        var grid = this.FindControl<Grid>("MainContentGrid");
        if (grid == null) return;

        bool visible = (_subscribedViewModel?.IsStatsVisible) ?? false;
        grid.RowDefinitions[1].Height = visible ? new GridLength(5) : new GridLength(0);
        grid.RowDefinitions[2].Height = visible ? new GridLength(100) : new GridLength(0);
    }

    // ── Sort-header text ───────────────────────────────────────────────────

    /// <summary>
    /// Updates the DataGrid column header strings to reflect the current sort
    /// column and direction (e.g. "Name ↑").
    /// </summary>
    private void ApplySortHeaders(FolderDiffViewModel vm)
    {
        // Column order: 0 = expand/collapse, 1 = Name, 2 = Size, 3 = Type, 4 = Time
        foreach (var grid in new[] { LeftDataGrid, RightDataGrid })
        {
            if (grid.Columns.Count < 5) continue;
            grid.Columns[1].Header = vm.NameSortHeader;
            grid.Columns[2].Header = vm.SizeSortHeader;
            grid.Columns[3].Header = vm.TypeSortHeader;
            grid.Columns[4].Header = vm.TimeSortHeader;
        }
    }

    // ── Column-header click → sort ─────────────────────────────────────────

    private void OnDataGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not FolderDiffViewModel vm) return;
        if (sender is not DataGrid grid) return;
        if (e.Source is not Visual source) return;

        // If the click is inside a data cell/row it is NOT a header click.
        if (source.FindAncestorOfType<DataGridCell>(includeSelf: true) != null) return;
        if (source.FindAncestorOfType<DataGridRow>(includeSelf: true)  != null) return;

        // Must be inside a column header.
        if (source.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) == null) return;

        // Use the pointer X-position to identify the clicked column.
        var pt = e.GetPosition(grid);
        double accumulated = 0;
        for (int i = 0; i < grid.Columns.Count; i++)
        {
            accumulated += grid.Columns[i].ActualWidth;
            if (pt.X < accumulated)
            {
                // Strip any existing sort indicator to get the base column name.
                var columnName = grid.Columns[i].Header?.ToString()
                    ?.Replace(FolderDiffViewModel.AscendingIndicator,  "")
                    .Replace(FolderDiffViewModel.DescendingIndicator, "")
                    .Trim();
                vm.SortByColumnCommand.Execute(columnName);
                return;
            }
        }
    }

    // ── Grid wiring (row colours + scroll sync) ─────────────────────────────

    private void SetupGrids()
    {
        // The handlers below must only be added once, even if the view is re-attached.
        if (_gridsSetUp) return;

        var leftGrid = this.FindControl<DataGrid>("LeftDataGrid");
        var rightGrid = this.FindControl<DataGrid>("RightDataGrid");
        if (leftGrid == null || rightGrid == null) return;
        _gridsSetUp = true;

        // Track which side was last interacted with ──────────────────────────
        leftGrid.PointerPressed  += (_, _) => { if (_subscribedViewModel != null) _subscribedViewModel.SelectedSide = FolderSide.Left; };
        rightGrid.PointerPressed += (_, _) => { if (_subscribedViewModel != null) _subscribedViewModel.SelectedSide = FolderSide.Right; };

        // Column-header sort clicks ──────────────────────────────────────────
        leftGrid.AddHandler(PointerReleasedEvent, OnDataGridPointerReleased, RoutingStrategies.Tunnel);
        rightGrid.AddHandler(PointerReleasedEvent, OnDataGridPointerReleased, RoutingStrategies.Tunnel);

        // Vertical scroll synchronisation ─────────────────────────────────────
        // DataGrid.ProcessVerticalScroll(ScrollEventType) is the actual scroll
        // pipeline: it calls ScrollSlotIntoView + InvalidateRowsMeasure to move
        // the virtualised rows.  It reads the current _vScrollBar.Value to
        // determine the target row, so we must pre-set it before invoking.
        //
        // We detect the change via ValueChanged (fired when the DataGrid updates
        // its own scrollbar after a user scroll), pre-set the target scrollbar's
        // Value, then invoke ProcessVerticalScroll on the target grid.
        //
        // _isSyncing blocks the re-entrant ValueChanged fired by ProcessVerticalScroll
        // when it internally updates the scrollbar.
        ScrollBar? leftVSb = null;
        ScrollBar? rightVSb = null;

        leftGrid.TemplateApplied += (_, args) =>
        {
            leftVSb = args.NameScope.Find<ScrollBar>("PART_VerticalScrollbar");
            if (leftVSb != null)
                leftVSb.ValueChanged += (_, _) =>
                {
                    if (_isSyncing || rightVSb == null) return;
                    _isSyncing = true;
                    try
                    {
                        rightVSb.Value = leftVSb.Value;
                        s_processVerticalScroll?.Invoke(rightGrid, new object[] { ScrollEventType.ThumbTrack });
                    }
                    catch (TargetInvocationException ex) { System.Diagnostics.Debug.WriteLine($"[FolderDiffView] Scroll sync (L→R) failed: {ex.InnerException ?? ex}"); }
                    finally { _isSyncing = false; }
                };
        };

        rightGrid.TemplateApplied += (_, args) =>
        {
            rightVSb = args.NameScope.Find<ScrollBar>("PART_VerticalScrollbar");
            if (rightVSb != null)
                rightVSb.ValueChanged += (_, _) =>
                {
                    if (_isSyncing || leftVSb == null) return;
                    _isSyncing = true;
                    try
                    {
                        leftVSb.Value = rightVSb.Value;
                        s_processVerticalScroll?.Invoke(leftGrid, new object[] { ScrollEventType.ThumbTrack });
                    }
                    catch (TargetInvocationException ex) { System.Diagnostics.Debug.WriteLine($"[FolderDiffView] Scroll sync (R→L) failed: {ex.InnerException ?? ex}"); }
                    finally { _isSyncing = false; }
                };
        };
    }

    // ── View mode ──────────────────────────────────────────────────────────

    /// <summary>
    /// Updates column widths on the path-labels grid and the DataGrids panel
    /// to show only the selected directory side (or both).
    /// </summary>
    private void UpdateViewMode()
    {
        var mode = _subscribedViewModel?.CurrentViewMode ?? ViewMode.Both;
        var pathLabelsGrid = this.FindControl<Grid>("PathLabelsGrid");
        var dataGridsPanel = this.FindControl<Grid>("DataGridsPanel");

        UpdateTwoSideGrid(pathLabelsGrid, mode);
        UpdateTwoSideGrid(dataGridsPanel, mode);
    }

    private static void UpdateTwoSideGrid(Grid? grid, ViewMode mode)
    {
        if (grid == null || grid.ColumnDefinitions.Count < 3) return;

        switch (mode)
        {
            case ViewMode.Both:
                grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                grid.ColumnDefinitions[1].Width = new GridLength(4);
                grid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                break;
            case ViewMode.OnlyLeft:
                grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                grid.ColumnDefinitions[1].Width = new GridLength(0);
                grid.ColumnDefinitions[2].Width = new GridLength(0);
                break;
            case ViewMode.OnlyRight:
                grid.ColumnDefinitions[0].Width = new GridLength(0);
                grid.ColumnDefinitions[1].Width = new GridLength(0);
                grid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                break;
        }
    }

    // ── Clipboard ──────────────────────────────────────────────────────────

    private async void OnCopyToClipboardRequested(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    // ── Error notification ─────────────────────────────────────────────────

    private async void OnDeleteErrorOccurred(string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return;

        var okBtn = new Button { Content = "OK", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Center };

        var dialog = new Window
        {
            Title  = "Delete Error",
            Width  = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin  = new Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
                    okBtn
                }
            }
        };

        okBtn.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    // ── Confirm dialog ─────────────────────────────────────────────────────

    private async Task<bool> ShowConfirmDialogAsync(string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner == null) return false;

        var result = false;

        var yesBtn = new Button { Content = "Delete", MinWidth = 80 };
        var noBtn  = new Button { Content = "Cancel",  MinWidth = 80 };

        var dialog = new Window
        {
            Title  = "Confirm Delete",
            Width  = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin  = new Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text        = message,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth    = 380
                    },
                    new StackPanel
                    {
                        Orientation       = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing           = 8,
                        Children          = { yesBtn, noBtn }
                    }
                }
            }
        };

        yesBtn.Click += (_, _) => { result = true; dialog.Close(); };
        noBtn.Click  += (_, _) => dialog.Close();

        await dialog.ShowDialog(owner);
        return result;
    }

    // ── Double-click → open file diff ──────────────────────────────────────

    private void OnGridDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FolderDiffViewModel vm) return;
        if (sender is not DataGrid grid) return;
        if (grid.SelectedItem is not FolderNodeViewModel node) return;
        if (!node.IsFile) return; // Folders are expanded/collapsed via the toggle button

        vm.SelectedNode = node;
        vm.OpenSelectedNode();
    }

    // ── FolderOpened (expand node from outside) ────────────────────────────

    private void OnFolderOpened(FolderNodeViewModel openedNode)
    {
        // Setting IsExpanded triggers FolderDiffViewModel.OnNodePropertyChanged
        // which rebuilds FlatRows, making both DataGrids update automatically.
        openedNode.IsExpanded = true;
    }

    // ── FileEntrySelected (open file diff window) ──────────────────────────

    private void OnFileEntrySelected(string leftPath, string rightPath)
    {
        if (!IsPathSelectionSafe(leftPath, rightPath))
            return;

        if (string.IsNullOrEmpty(leftPath) && string.IsNullOrEmpty(rightPath))
            return;

        var vm = new FileDiffViewModel();
        try
        {
            // A missing side (empty path) is compared as an empty file.
            vm.LoadFiles(leftPath, rightPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read '{leftPath}' / '{rightPath}'");
            System.Diagnostics.Debug.WriteLine(ex);
            return;
        }

        var window = new Window
        {
            Title = "Codiff - File Compare",
            Width = 1280,
            Height = 960,
            MinWidth = 800,
            MinHeight = 500,
            Content = new FileDiffView
            {
                DataContext = vm
            }
        };

        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    private bool IsPathSelectionSafe(string leftPath, string rightPath)
    {
        if (DataContext is not FolderDiffViewModel vm)
            return false;

        if (!string.IsNullOrEmpty(leftPath) &&
            !IsUnderFolder(leftPath, vm.LeftFolderPath))
            return false;

        if (!string.IsNullOrEmpty(rightPath) &&
            !IsUnderFolder(rightPath, vm.RightFolderPath))
            return false;

        return true;
    }

    private static bool IsUnderFolder(string filePath, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(folderPath))
            return false;

        var fullFilePath = Path.GetFullPath(filePath);
        if (!File.Exists(fullFilePath))
            return false;

        var fullFolderPath = Path.GetFullPath(folderPath);
        var relativePath = Path.GetRelativePath(fullFolderPath, fullFilePath);

        if (Path.IsPathRooted(relativePath))
            return false;

        return !relativePath.Equals("..", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
